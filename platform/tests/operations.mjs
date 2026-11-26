import assert from 'node:assert/strict'
import { execFileSync } from 'node:child_process'
import { randomUUID } from 'node:crypto'

const base = 'http://127.0.0.1:5096/api'
async function client(login, password = process.env.Bootstrap__Password) {
  const cookies = new Map()
  let token
  async function call(path, method = 'GET', data, expected = 200) {
    const headers = { 'Content-Type': 'application/json', Cookie: [...cookies.values()].join('; ') }
    if (token) headers['X-CSRF-Token'] = token
    const response = await fetch(base + path, { method, headers, body: JSON.stringify(data) })
    for (const set of response.headers.getSetCookie()) {
      const cookie = set.split(';')[0]
      cookies.set(cookie.split('=')[0], cookie)
    }
    const body = await response.text()
    assert.ok([expected].flat().includes(response.status), path + ' ' + response.status + ' ' + body)
    return body && JSON.parse(body)
  }
  if (login) {
    token = (await call('/auth/csrf')).token
    await call('/auth/login', 'POST', { login, password })
    token = (await call('/auth/csrf')).token
  }
  return call
}
const admin = await client('test-admin')
const coach = await client('test-coach')
const guest = await client()
const pilot = await coach('/staff/pilots', 'POST', { firstName: 'Тест', lastName: 'Тестов', login: 'test-pilot', password: process.env.Bootstrap__Password }, 201)
const today = new Intl.DateTimeFormat('sv-SE', { timeZone: 'Europe/Moscow' }).format(new Date())
const monday = new Date(today + 'T12:00:00Z')
monday.setUTCDate(monday.getUTCDate() - (monday.getUTCDay()+6)%7)
const date = monday.toISOString().slice(0,10)
const booking = await coach('/staff/schedule','POST',{ pilotId:pilot.id, startsOn:date, startHour:12 },201)
const visit = { date, bookingId:booking.id, minutes:75 }
const path = '/staff/pilots/' + pilot.id
const [first, second] = await Promise.all([coach, admin].map(send =>
  send(path + '/attendance', 'POST', { ...visit, operationId: randomUUID() }, [200, 201])))
assert.equal(first.id,second.id)
assert.equal((await coach(path+'/training')).practiceMinutes,75)
assert.equal((await coach('/staff/schedule?week='+date))[0].bookings[0].minutes,75)
const challenge = await admin('/admin/challenges','POST',{month:today.slice(0,7)+'-01', simulator:'Test',car:'Car',track:'Track',rules:'Time'},201)
const lap = await coach('/staff/challenges/'+challenge.id+'/laps','POST',{ pilotId:pilot.id, milliseconds:70000 },201)
assert.equal((await guest('/public/leaderboard')).standings.length,0)
const instructorLap = '/staff/challenges/' + challenge.id + '/instructor-lap'
await guest(instructorLap, 'PUT', { milliseconds: 65000 }, 401)
await coach(instructorLap, 'PUT', { milliseconds: 0 }, 400)
await coach(instructorLap, 'PUT', { milliseconds: 65000 }, 204)
assert.equal((await guest('/public/leaderboard')).instructorLapMilliseconds, 65000)
const standingsWithInstructor = await coach('/staff/challenges/current')
assert.deepEqual(standingsWithInstructor.standings.map(row => [row.place, row.milliseconds, row.gapMilliseconds]), [[1, 70000, 0]])
await coach(instructorLap, 'PUT', { milliseconds: null }, 204)
assert.equal((await guest('/public/leaderboard')).instructorLapMilliseconds, null)
const cancel='/admin/pilots/'+pilot.id+'/void/lap/'+lap.id
await coach(cancel,'PUT',{reason:'Опечатка'},403)
await admin(cancel,'PUT',{reason:'Опечатка'},204)
assert.equal((await coach(path+'/training')).bestLapMilliseconds,null)
const task=await admin('/admin/tasks','POST',{stage:1,order:1,title:'Упражнение',instructions:'Заезд',resultType:'manual',normative:'По решению тренера'},201)
const attempt=await coach(path+'/attempts','POST',{taskId:task.id,result:'Готово'},201)
await coach('/staff/attempts/'+attempt.id+'/decision','PUT',{passed:true},204)
await admin('/admin/pilots/'+pilot.id+'/void/attempt/'+attempt.id,'PUT',{reason:'Неверный ученик'},204)
assert.equal((await coach(path+'/training')).tasks[0].completed,false)
assert((await coach(path+'/history')).some(e=>e.detail==='Неверный ученик'))
console.log('PASS: attendance, role isolation, corrections, progress, history, private standings')

const learner = await client('test-pilot')
const race = await admin('/admin/tasks', 'POST', {
  stage: 1, order: 2, title: 'Круг', instructions: 'Чистый круг', resultType: 'time', normative: '1:45.000'
}, 201)
const standings = '/pilot/tasks/' + race.id + '/standings'
await guest(standings, 'GET', undefined, 401)
await coach(standings, 'GET', undefined, 403)
assert.deepEqual((await learner(standings)).standings, [])
const instructorTasks = '/staff/training/instructor'
const instructorTime = instructorTasks + '/' + race.id
await learner(instructorTime, 'PUT', { result: '1:37.500' }, 403)
await coach(instructorTime, 'PUT', { result: '1:60.000' }, 400)
await coach(instructorTasks + '/' + task.id, 'PUT', { result: '1:37.500' }, 404)
await coach(instructorTime, 'PUT', { result: '1:38.500' }, 204)
await coach(instructorTime, 'PUT', { result: '1:37.500' }, 204)
assert.deepEqual((await coach(instructorTasks)).map(t => [t.id, t.instructorLapMilliseconds]), [[race.id, 97500]])
assert.equal((await learner(standings)).instructorLapMilliseconds, 97500)
assert.equal((await learner('/pilot/me')).tasks.find(t => t.task.id === race.id).completed, false)
await coach(path + '/tasks/' + race.id + '/unlock', 'PUT', undefined, 204)
for (const [result, passed] of [['1:42.500', true], ['1:40.125', true], ['1:39.000', null], ['1:38.000', false]]) {
  const run = await coach(path + '/attempts', 'POST', { taskId: race.id, result }, 201)
  if (passed !== null) await coach('/staff/attempts/' + run.id + '/decision', 'PUT', { passed }, 204)
}
assert.deepEqual((await learner(standings)).standings, [
  { place: 1, name: 'Тест Т.', result: '100125', isSelf: true }
])
console.log('PASS: private task leaderboard includes only best passing results')

await coach('/admin/accounts', 'GET', undefined, 403)
const accounts = await admin('/admin/accounts')
const coachAccount = accounts.find(account => account.role === 'coach')
const pilotCredentials = '/admin/accounts/' + pilot.id + '/credentials'
const coachCredentials = '/admin/accounts/' + coachAccount.id + '/credentials'
await coach(pilotCredentials, 'PUT', { login: 'another-pilot' }, 403)
await admin(pilotCredentials, 'PUT', { login: ' TEST-COACH ' }, 409)
await admin(pilotCredentials, 'PUT', { login: ' Nova ' }, 204)
await learner('/pilot/me', 'GET', undefined, 401)
const renamedPilot = await client('nova')
assert.equal((await renamedPilot('/pilot/me')).id, pilot.id)
assert.equal((await renamedPilot('/pilot/me')).practiceMinutes, 75)
await admin(coachCredentials, 'PUT', { login: 'trainer', password: 'too-short' }, 400)
const newCoachPassword = randomUUID()
await admin(coachCredentials, 'PUT', { login: 'trainer', password: newCoachPassword }, 204)
await coach('/staff/pilots', 'GET', undefined, 401)
const renamedCoach = await client('trainer', newCoachPassword)
assert.equal((await renamedCoach('/auth/me')).id, coachAccount.id)
for (const login of ['test-coach', 'trainer', 'test-pilot'])
  await assert.rejects(client(login), /401/)
console.log('PASS: administrator can rename pilots and change trainer credentials')

const anotherCoach = await client('trainer', newCoachPassword)
function maintain(login, command) {
  execFileSync('dotnet', [process.env.TEST_API_DLL, command], {
    env: { ...process.env, Bootstrap__Login: login }, stdio: 'pipe'
  })
}
maintain('trainer', '--disable-staff')
for (const current of [renamedCoach, anotherCoach]) await current('/staff/pilots', 'GET', undefined, 401)
maintain('test-admin', '--recover-staff')
await admin('/staff/pilots', 'GET', undefined, 401)
await (await client('test-admin'))('/staff/pilots')
console.log('PASS: staff revocation on both devices and administrator recovery')

import assert from 'node:assert/strict'
import { execFileSync } from 'node:child_process'
import { randomUUID } from 'node:crypto'

const base = 'http://127.0.0.1:5096/api'
async function client(login) {
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
    await call('/auth/login', 'POST', { login, password: process.env.Bootstrap__Password })
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
await coach(path + '/tasks/' + race.id + '/unlock', 'PUT', undefined, 204)
for (const [result, passed] of [['1:42.500', true], ['1:40.125', true], ['1:39.000', null], ['1:38.000', false]]) {
  const run = await coach(path + '/attempts', 'POST', { taskId: race.id, result }, 201)
  if (passed !== null) await coach('/staff/attempts/' + run.id + '/decision', 'PUT', { passed }, 204)
}
assert.deepEqual((await learner(standings)).standings, [
  { place: 1, name: 'Тест Т.', result: '100125', isSelf: true }
])
console.log('PASS: private task leaderboard includes only best passing results')

const anotherCoach = await client('test-coach')
function maintain(login, command) {
  execFileSync('dotnet', [process.env.TEST_API_DLL, command], {
    env: { ...process.env, Bootstrap__Login: login }, stdio: 'pipe'
  })
}
maintain('test-coach', '--disable-staff')
for (const current of [coach, anotherCoach]) await current('/staff/pilots', 'GET', undefined, 401)
maintain('test-admin', '--recover-staff')
await admin('/staff/pilots', 'GET', undefined, 401)
await (await client('test-admin'))('/staff/pilots')
console.log('PASS: staff revocation on both devices and administrator recovery')

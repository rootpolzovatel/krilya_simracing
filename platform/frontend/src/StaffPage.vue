<script setup>
import { computed, onMounted, ref, watch } from 'vue'
import ChallengeCard from './ChallengeCard.vue'
import StaffPilotOptions from './StaffPilotOptions.vue'
import StandingsTable from './StandingsTable.vue'
import TrainingLessons from './TrainingLessons.vue'
import { lapTime, monthLabel, moscowDate, parseLap, request, session, write } from './api.js'

const tabs = [
  ['schedule', 'Расписание'], ['pilots', 'Ученики'], ['training', 'Программа обучения'], ['monthly', 'Испытание месяца'],
]
const tab = ref('schedule')
const pilots = ref([])
const bookings = ref([])
const currentLessonIds = ref([])
const competition = ref(null)
const selected = ref('')
const instructorSelected = ref(false)
const trainingSelection = computed({
  get: () => instructorSelected.value ? 'instructor' : selected.value || 'instructor',
  set: value => {
    instructorSelected.value = value === 'instructor'
    if (!instructorSelected.value) selected.value = value
  },
})
const instructorTraining = computed(() => trainingSelection.value === 'instructor')
const instructorTasks = ref(null)
const profile = ref(null)
const trainingTasks = computed(() => instructorTraining.value ? instructorTasks.value : profile.value?.tasks)
const history = ref([])
const busy = ref(false)
const error = ref('')
const notice = ref('')
const dialog = ref()
const action = ref('')
const draft = ref({})
const formError = ref('')
const taskResults = ref({})
const taskOperations = new Map()
const hours = Array.from({ length: 10 }, (_, index) => index + 9)
const formatHours = minutes => new Intl.NumberFormat('ru', { maximumFractionDigits: 2 }).format(minutes / 60) + ' ч'
const totalPractice = computed(() => pilots.value.reduce((total, pilot) => total + pilot.practiceMinutes, 0))
const titles = { pilot: 'Новый ученик', schedule: 'Новое занятие', attendance: 'Отметить занятие',
  lap: 'Добавить результат', instructor: 'Результат инструктора' }
const currentLessonPilots = computed(() => pilots.value.filter(pilot => currentLessonIds.value.includes(pilot.id)))
const otherPilots = computed(() => pilots.value.filter(pilot => !currentLessonIds.value.includes(pilot.id)))

function operationId() {
  // randomUUID требует HTTPS, а getRandomValues доступен и на HTTP.
  const bytes = crypto.getRandomValues(new Uint8Array(16))
  bytes[6] = (bytes[6] & 15) | 64
  bytes[8] = (bytes[8] & 63) | 128
  const hex = Array.from(bytes, byte => byte.toString(16).padStart(2, '0')).join('')
  return [hex.slice(0, 8), hex.slice(8, 12), hex.slice(12, 16), hex.slice(16, 20), hex.slice(20)].join('-')
}

function offset(date, days) {
  const instant = new Date(date + 'T12:00:00Z')
  instant.setUTCDate(instant.getUTCDate() + days)
  return instant.toISOString().slice(0, 10)
}
function monday(today = moscowDate()) {
  return offset(today, -((new Date(today + 'T12:00:00Z').getUTCDay() + 6) % 7))
}
const week = ref(monday())
const todayOnly = ref(false)
const days = computed(() => Array.from({ length: 5 }, (_, index) => offset(week.value, index)).filter(day => !todayOnly.value || day === moscowDate()))
const dayNames = ['Понедельник', 'Вторник', 'Среда', 'Четверг', 'Пятница']
const dayLabel = day => new Intl.DateTimeFormat('ru', { day: 'numeric', month: 'short', timeZone: 'UTC' })
  .format(new Date(day + 'T12:00:00Z'))
const timeLabel = hour => String(hour).padStart(2, '0') + ':00'
const name = id => {
  const pilot = pilots.value.find(item => item.id === id)
  return pilot ? pilot.firstName + ' ' + pilot.lastName : 'Ученик'
}
const dayBookings = date => bookings.value.filter(item => item.date === date)

async function refreshPilots() {
  pilots.value = await request('/staff/pilots')
  if (!selected.value && pilots.value.length) selected.value = pilots.value[0].id
}
async function refreshWeek() {
  const requested = week.value
  const data = await request('/staff/schedule?week=' + requested)
  if (week.value === requested) bookings.value = data
}
async function refreshCurrentLesson() {
  const now = new Date()
  const date = moscowDate(now)
  const hour = Number(new Intl.DateTimeFormat('en-GB', {
    hour: '2-digit', hourCycle: 'h23', timeZone: 'Europe/Moscow',
  }).format(now))
  const schedule = await request('/staff/schedule?week=' + monday(date))
  currentLessonIds.value = schedule.find(slot => slot.date === date && slot.startHour === hour)
    ?.bookings.map(booking => booking.pilotId) ?? []
}
async function refreshCompetition() {
  try { competition.value = await request('/staff/challenges/current') }
  catch (reason) {
    if (reason.status !== 404) throw reason
    competition.value = null
  }
}
async function refreshProfile(reset = true) {
  const id = trainingSelection.value
  if (reset) {
    profile.value = null
    instructorTasks.value = null
    history.value = []
  }
  if (id === 'instructor') {
    const tasks = await request('/staff/training/instructor')
    if (trainingSelection.value === id)
      instructorTasks.value = tasks.map(task => ({ task, open: true, attempts: [] }))
    return
  }
  const data = await request('/staff/pilots/' + id + '/training')
  const events = await request('/staff/pilots/' + id + '/history')
  if (trainingSelection.value === id) { profile.value = data; history.value = events }
}
async function attempt(work) {
  error.value = ''
  try { await work() } catch (reason) { error.value = reason.message }
}
onMounted(() => attempt(() => Promise.all([refreshPilots(), refreshWeek(), refreshCompetition()])))
watch(week, () => attempt(refreshWeek))
watch(trainingSelection, () => { taskResults.value = {} })
watch([tab, trainingSelection], () => {
  if (tab.value === 'training') attempt(refreshProfile)
})
watch(tab, () => {
  if (tab.value === 'training') attempt(refreshCurrentLesson)
  if (tab.value === 'monthly') attempt(() => Promise.all([refreshCompetition(), refreshCurrentLesson()]))
})

function open(kind, values = {}) {
  action.value = kind
  const base = kind === 'pilot' ? { firstName: '', lastName: '', login: '', password: '' }
    : kind === 'schedule' ? { pilotId: selected.value, startsOn: week.value, startHour: 16 }
    : kind === 'attendance' ? { pilotId: selected.value, date: moscowDate(), minutes: 60, operationId: operationId() }
    : kind === 'instructor' ? { time: competition.value.instructorLapMilliseconds
      ? lapTime(competition.value.instructorLapMilliseconds) : '', challengeId: competition.value.challenge.id }
    : { pilotId: '', time: '', challengeId: competition.value?.challenge.id, operationId: operationId() }
  draft.value = { ...base, ...values }
  formError.value = ''
  dialog.value.showModal()
  if (kind === 'lap') refreshCurrentLesson().catch(reason => { error.value = reason.message })
}

async function submit() {
  busy.value = true
  formError.value = ''
  const input = draft.value
  const kind = action.value
  try {
    switch (kind) {
      case 'pilot':
        await write('POST', '/staff/pilots', input)
        break
      case 'schedule':
        await write('POST', '/staff/schedule', input)
        break
      case 'attendance': {
        const { pilotId, ...body } = input
        await write('POST', '/staff/pilots/' + pilotId + '/attendance', body)
        break
      }
      case 'lap':
        await write('POST', '/staff/challenges/' + input.challengeId + '/laps',
          { pilotId: input.pilotId, milliseconds: parseLap(input.time), operationId: input.operationId })
        break
      case 'instructor':
        await write('PUT', '/staff/challenges/' + input.challengeId + '/instructor-lap',
          { milliseconds: input.time ? parseLap(input.time) : null })
        break
    }
    dialog.value.close()
    notice.value = 'Сохранено.'
    await attempt(async () => {
      if (kind === 'attendance') await refreshWeek()
      if (kind === 'schedule') await refreshWeek()
      else await refreshPilots()
      if (kind === 'lap' || kind === 'instructor') await refreshCompetition()
      if (tab.value === 'training') await refreshProfile()
    })
  } catch (reason) {
    formError.value = reason.message
  } finally {
    busy.value = false
  }
}

async function changeBooking(booking, date, change) {
  const label = change === 'skip' ? 'Пропустить занятие' : 'Убрать будущие занятия начиная с этой даты'
  if (!window.confirm(label + ' для ученика ' + name(booking.pilotId) + '?')) return
  await attempt(async () => {
    await write('PUT', '/staff/schedule/' + booking.id + '/' + change + '/' + date)
    await refreshWeek()
  })
}

async function saveResult(task) {
  busy.value = true
  const pilotId = trainingSelection.value
  const result = taskResults.value[task.id]
  try {
    await attempt(async () => {
      if (pilotId === 'instructor') {
        await write('PUT', '/staff/training/instructor/' + task.id, { result })
      } else {
        const key = pilotId + '/' + task.id
        let operation = taskOperations.get(key)
        if (!operation || operation.result !== result) {
          operation = { id: operationId(), result }
          taskOperations.set(key, operation)
        }
        await write('POST', '/staff/pilots/' + pilotId + '/attempts',
          { taskId: task.id, result, operationId: operation.id })
        taskOperations.delete(key)
      }
      if (trainingSelection.value === pilotId) {
        delete taskResults.value[task.id]
        await refreshProfile(false)
      }
    })
  } finally { busy.value = false }
}

async function decide(id, passed) {
  busy.value = true
  try {
    await attempt(async () => {
      await write('PUT', '/staff/attempts/' + id + '/decision', { passed })
      await refreshProfile()
      await refreshPilots()
    })
  } finally { busy.value = false }
}

async function voidEntry(event) {
  const reason = window.prompt('Причина аннулирования')
  if (!reason?.trim()) return
  const id = selected.value
  await attempt(async () => {
    await write('PUT', '/admin/pilots/' + id + '/void/' + event.kind + '/' + event.id, { reason })
    await Promise.all([refreshPilots(), refreshProfile(), refreshCompetition()])
  })
}

async function unlock(taskId) {
  busy.value = true
  const pilotId = selected.value
  try {
    await attempt(async () => {
      await write('PUT', '/staff/pilots/' + pilotId + '/tasks/' + taskId + '/unlock')
      await refreshProfile()
    })
  } finally { busy.value = false }
}
</script>

<template>
  <header class="page-heading">
    <div><div class="eyebrow">УПРАВЛЕНИЕ СЕКЦИЕЙ</div><h1>Панель тренера</h1><p>Общая база учеников. Все ученики доступны каждому тренеру.</p></div>
    <button class="primary" @click="open('pilot')">+ Добавить ученика</button>
  </header>
  <div class="stats">
    <div class="stat"><span>Учеников</span><strong>{{ pilots.length }}</strong></div>
    <div class="stat"><span>Тренировочных часов</span><strong>{{ formatHours(totalPractice) }}</strong></div>
    <div class="stat"><span>Активное испытание</span><strong>{{ competition ? monthLabel(competition.challenge.month) : '—' }}</strong></div>
  </div>
  <div class="tabs" aria-label="Разделы панели тренера">
    <button v-for="[key, title] in tabs" :key="key" class="quiet" :class="{ active: tab === key }"
      type="button" :aria-pressed="tab === key" @click="tab = key; notice = ''">{{ title }}</button>
  </div>
  <p v-if="error" class="error" role="alert">{{ error }}</p>
  <p v-if="notice" class="success" role="status">{{ notice }}</p>

  <section v-if="tab === 'schedule'" class="panel results" aria-label="Расписание">
    <div class="panel-head">
      <div><h2>Расписание</h2><p class="schedule-caption">Пн–пт · 09:00–19:00 · занятие 1 час · МСК</p></div>
      <button class="primary" :disabled="!pilots.length" @click="open('schedule')">+ Добавить занятие</button>
    </div>
    <div class="week-toolbar">
      <div class="week-controls">
        <button class="quiet" type="button" aria-label="Предыдущая неделя" @click="todayOnly = false; week = offset(week, -7)">←</button>
        <span class="week-title">{{ dayLabel(week) }} — {{ dayLabel(offset(week, 4)) }}</span>
        <button class="quiet" type="button" aria-label="Следующая неделя" @click="todayOnly = false; week = offset(week, 7)">→</button>
      </div>
      <button class="quiet" type="button" @click="todayOnly = false; week = monday()">Текущая неделя</button>
      <button class="quiet" :aria-pressed="todayOnly" @click="week = monday(); todayOnly = true">Сегодня</button>
    </div>
    <p v-if="todayOnly && !days.length" class="empty">Сегодня выходной. Занятий по расписанию нет.</p>
    <div class="schedule-grid">
      <section v-for="day in days" :key="day" class="schedule-day" :class="{ 'is-today': day === moscowDate() }">
        <div class="schedule-day-head"><h3>{{ dayNames[new Date(day + 'T12:00:00Z').getUTCDay() - 1] }}</h3><time :datetime="day">{{ dayLabel(day) }}</time></div>
        <div class="schedule-entries">
          <p v-if="!dayBookings(day).length" class="schedule-empty">Занятий пока нет</p>
          <article v-for="slot in dayBookings(day)" :key="slot.startHour" class="appointment appointment-group">
            <div class="appointment-head"><strong>{{ timeLabel(slot.startHour) }} — {{ timeLabel(slot.startHour + 1) }}</strong>
              <small :aria-label="'Занято мест: ' + slot.bookings.length + ' из 2'">{{ slot.bookings.length }}/2</small></div>
            <div v-for="booking in slot.bookings" :key="booking.id" class="appointment-student">
              <div class="appointment-student-info"><button type="button" class="student-link" @click="selected = booking.pilotId; tab = 'training'">{{ name(booking.pilotId) }}</button><small>Еженедельно</small></div>
              <span v-if="booking.minutes">{{ booking.minutes }} мин · отмечено</span>
              <div class="appointment-actions">
                <button v-if="!booking.minutes && day <= moscowDate()" type="button" class="quiet appointment-attendance" @click="open('attendance', { pilotId: booking.pilotId, date: day, bookingId: booking.id })">Отметить посещение</button>
                <button type="button" class="lesson-icon" :aria-label="'Пропустить занятие: ' + name(booking.pilotId)" title="Пропустить занятие" @click="changeBooking(booking, day, 'skip')">
                  <svg viewBox="0 0 24 24" width="16" height="16" aria-hidden="true"><path d="M8 5l8 7-8 7V5zM19 5v14" /></svg>
                </button>
                <button type="button" class="lesson-icon stop-series" :aria-label="'Убрать из расписания: ' + name(booking.pilotId)" title="Убрать из расписания с этой даты" @click="changeBooking(booking, day, 'end')">
                  <svg viewBox="0 0 24 24" width="16" height="16" aria-hidden="true"><path d="M4 7h16M9 7V4h6v3M6 7l1 13h10l1-13M10 11v5M14 11v5" /></svg>
                </button>
              </div>
            </div>
          </article>
        </div>
        <button type="button" class="day-add" :disabled="!pilots.length" :aria-label="'Добавить занятие: ' + dayLabel(day)" @click="open('schedule', { startsOn: day })">+ Занятие</button>
      </section>
    </div>
  </section>

  <section v-else-if="tab === 'pilots'" class="panel results" aria-label="Ученики">
    <div class="panel-head"><h2>Ученики секции</h2><div class="actions">
      <button class="quiet" :disabled="!pilots.length" @click="open('attendance')">Внеплановое посещение</button>
      <button class="primary" :disabled="!competition || !pilots.length" @click="open('lap')">+ Результат</button>
    </div></div>
    <p v-if="!pilots.length" class="empty">Список учеников пока пуст.</p>
    <div v-else class="table-scroll" tabindex="0" aria-label="Список учеников">
      <table class="pilot-table"><thead><tr><th scope="col">УЧЕНИК</th><th scope="col">ЭТАП</th><th scope="col">ПРАКТИКА</th>
        <th scope="col">КРУГ МЕСЯЦА</th></tr></thead>
        <tbody><tr v-for="pilot in pilots" :key="pilot.id">
          <td><button type="button" class="student-link" :aria-label="'Учебный путь: ' + name(pilot.id)" @click="trainingSelection = pilot.id; tab = 'training'">{{ name(pilot.id) }}</button></td>
          <td class="student-stage"><template v-if="pilot.stage">{{ String(pilot.stage).padStart(2, '0') }} · {{ pilot.stageTitle ?? 'Этап ' + pilot.stage }}
            <small>{{ pilot.stageCompleted }} / {{ pilot.stageTotal }} заданий</small></template><span v-else>—</span></td>
          <td class="number">{{ formatHours(pilot.practiceMinutes) }}</td>
          <td class="time">{{ pilot.bestLapMilliseconds ? lapTime(pilot.bestLapMilliseconds) : '—' }}</td>
        </tr></tbody>
      </table>
    </div>
  </section>

  <section v-else-if="tab === 'training'" aria-label="Программа обучения">
    <section v-if="currentLessonPilots.length" class="panel content-panel" aria-label="Сейчас на занятии">
      <strong>Сейчас на занятии</strong>
      <div class="actions">
        <button v-for="pilot in currentLessonPilots" :key="pilot.id" type="button" class="quiet"
          :aria-pressed="trainingSelection === pilot.id" @click="trainingSelection = pilot.id">{{ name(pilot.id) }}</button>
      </div>
    </section>
    <div class="learning-toolbar">
      <label class="learning-picker">Ученик или инструктор
        <select v-model="trainingSelection">
          <StaffPilotOptions :current="currentLessonPilots" :others="otherPilots" />
          <option value="instructor">Инструктор</option>
        </select>
      </label>
      <button v-if="profile?.tasks.some(entry => entry.canUnlock)" class="quiet" :disabled="busy"
        @click="unlock(profile.tasks.find(entry => entry.canUnlock).task.id)">Открыть следующее задание</button>
    </div>
    <template v-if="trainingTasks">
      <div v-if="profile" class="section-title">
        <h2>Этап {{ profile.stage ? String(profile.stage).padStart(2, '0') : '—' }}<span v-if="profile.stageTitle"> · {{ profile.stageTitle }}</span></h2>
        <span class="tag">{{ profile.tasks.filter(entry => entry.completed).length }} / {{ profile.tasks.length }} заданий</span>
      </div>
      <TrainingLessons :tasks="trainingTasks" :instructor="instructorTraining" :key="trainingSelection" v-slot="{ entry }">
        <form v-if="entry.open" class="task-result-form" @submit.prevent="saveResult(entry.task)">
          <label>Фактический результат <input v-model.trim="taskResults[entry.task.id]" required maxlength="1000" :placeholder="entry.task.resultType === 'time' ? '1:39.500' : undefined"></label>
          <div class="actions"><button class="quiet" :disabled="busy">Сохранить результат</button></div>
        </form>
        <p v-if="instructorTraining && entry.task.instructorLapMilliseconds != null" class="attempt">
          Сохранённое время: <strong class="time">{{ lapTime(entry.task.instructorLapMilliseconds) }}</strong>
        </p>
        <div v-for="result in entry.attempts" :key="result.id" class="attempt heading-actions">
          <span><strong>{{ result.result }}</strong> · {{ result.passed === null ? 'Ожидает решения' : result.passed ? 'Выполнено' : 'Не выполнено' }}</span>
          <div v-if="result.passed === null" class="actions">
            <button class="primary" :disabled="busy" @click="decide(result.id, true)">Подтвердить выполнение</button>
            <button class="quiet" :disabled="busy" @click="decide(result.id, false)">Не выполнено</button>
          </div>
        </div>
      </TrainingLessons>
      <details v-if="profile" class="panel content-panel"><summary>История ученика</summary>
        <article v-for="event in history" :key="event.kind + event.id" class="rule">
          <strong>{{ ({lap: 'Круг', attempt: 'Учебная попытка'})[event.kind] ?? event.kind }}</strong>
          <p>{{ event.detail }}</p><small>{{ new Date(event.at).toLocaleString('ru', { timeZone: 'Europe/Moscow' }) }} МСК · {{ event.actor }}</small>
          <button v-if="session.user?.role === 'admin' && event.canVoid" class="quiet" @click="voidEntry(event)">Аннулировать</button>
        </article><p v-if="!history.length">Записей пока нет.</p>
      </details>
    </template>
  </section>

  <template v-else>
    <div v-if="competition" class="stack">
      <ChallengeCard :challenge="competition.challenge" />
      <section v-if="currentLessonPilots.length" class="panel content-panel" aria-label="Сейчас на занятии">
        <strong>Сейчас на занятии</strong>
        <div class="actions">
          <button v-for="pilot in currentLessonPilots" :key="pilot.id" type="button" class="quiet"
            @click="open('lap', { pilotId: pilot.id })">{{ name(pilot.id) }}</button>
        </div>
      </section>
      <StandingsTable :standings="competition.standings" :instructor-lap="competition.instructorLapMilliseconds" title="Результаты испытания">
        <template #actions>
          <div class="actions">
            <button class="primary" :disabled="!pilots.length" @click="open('lap')">+ Записать время</button>
            <button class="quiet" @click="open('instructor')">
              {{ competition.instructorLapMilliseconds ? 'Изменить ориентир' : '+ Результат инструктора' }}
            </button>
          </div>
        </template>
      </StandingsTable>
    </div>
    <p v-else class="panel empty">Испытание месяца ещё не опубликовано. Администратор может создать его вручную или настроить пары для жеребьёвки.</p>
  </template>

  <dialog ref="dialog" class="dialog" aria-labelledby="staff-dialog-title" @cancel.prevent="!busy && dialog.close()" @close="draft = {}">
    <form class="form" @submit.prevent="submit">
      <div class="eyebrow">{{ action === 'schedule' ? 'Расписание' : 'ПАНЕЛЬ ТРЕНЕРА' }}</div>
      <h2 id="staff-dialog-title">{{ titles[action] }}</h2>
      <template v-if="action === 'pilot'">
        <div class="form-grid">
          <label>Имя <input v-model.trim="draft.firstName" maxlength="100" required></label>
          <label>Фамилия <input v-model.trim="draft.lastName" maxlength="100" required></label>
        </div>
        <label>Логин <input v-model.trim="draft.login" autocomplete="off" minlength="3" maxlength="64" required></label>
        <label>Первоначальный пароль <input v-model="draft.password" type="password" autocomplete="new-password" minlength="8" maxlength="128" required></label>
      </template>
      <template v-else-if="action === 'instructor'">
        <label>Время круга инструктора <input v-model.trim="draft.time" placeholder="1:39.500"></label>
        <p class="muted small">Оставьте поле пустым, чтобы убрать ориентир из таблицы.</p>
      </template>
      <template v-else>
        <label>Ученик <select :disabled="!!draft.bookingId" v-model="draft.pilotId" required>
          <option value="" disabled>Выберите ученика</option>
          <StaffPilotOptions v-if="action === 'lap'" :current="currentLessonPilots" :others="otherPilots" />
          <StaffPilotOptions v-else :others="pilots" />
        </select></label>
        <template v-if="action === 'schedule'">
          <label>Дата первого занятия <input v-model="draft.startsOn" type="date" required></label>
          <label>Начало <select v-model="draft.startHour"><option v-for="hour in hours" :key="hour" :value="hour">{{ hour }}:00</option></select></label>
          <p class="muted small">Занятие будет повторяться каждую неделю.</p>
        </template>
        <template v-else-if="action === 'attendance'">
          <label>Дата посещения <input v-model="draft.date" :readonly="!!draft.bookingId" type="date" required></label>
          <label>Длительность, мин <input v-model.number="draft.minutes" type="number" min="1" max="1440" required></label>
        </template>
        <label v-else>Время чистого круга <input v-model.trim="draft.time" placeholder="1:39.500" required></label>
      </template>
      <p v-if="formError" class="error" role="alert">{{ formError }}</p>
      <div class="actions">
        <button type="button" class="quiet" :disabled="busy" @click="dialog.close()">Отмена</button>
        <button class="primary" :disabled="busy">{{ busy ? 'Сохраняем…' : action === 'schedule' ? 'Добавить занятие' : 'Сохранить' }}</button>
      </div>
    </form>
  </dialog>
</template>

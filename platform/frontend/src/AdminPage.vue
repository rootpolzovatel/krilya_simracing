<script setup>
import { computed, onMounted, ref } from 'vue'
import { monthLabel, moscowDate, request, write } from './api.js'

const tabs = [
  ['challenges', 'Ежемесячные испытания'], ['tasks', 'Учебные испытания'], ['ranks', 'Звания'],
]
const tab = ref('challenges')
const lists = ref({ challenges: [], tasks: [], ranks: [], pilots: [], stages: [], accounts: [] })
const dialog = ref()
const editing = ref('')
const input = ref({})
const error = ref('')
const formError = ref('')
const notice = ref('')
const busy = ref(false)
const month = computed(() => moscowDate().slice(0, 7))
const selectedImage = ref('')
const catalog = ref({ vehicles: [], circuits: [], pairs: [] })
const editingId = ref(null)
const selectedPair = ref('')
const resultTypes = { time: 'Время', points: 'Очки', place: 'Место', manual: 'Ручная проверка' }
const titles = {
  challenges: 'Создать испытание', tasks: 'Создать задание', ranks: 'Создать звание', images: 'Изображения испытания',
  vehicles: 'Добавить автомобиль', circuits: 'Добавить трассу', pairs: 'Настроить пару', credentials: 'Изменить данные для входа',
  stage: 'Назвать этап',
}
const editTitles = {
  challenges: 'Изменить испытание', tasks: 'Изменить задание', ranks: 'Изменить звание',
  vehicles: 'Изменить автомобиль', circuits: 'Изменить трассу',
}

const vehicleName = id => catalog.value.vehicles.find(vehicle => vehicle.id === id)?.name
const circuitName = id => catalog.value.circuits.find(circuit => circuit.id === id)?.label
const stageNumbers = computed(() => [...new Set(lists.value.tasks.map(task => task.stage))])
const stageName = number => lists.value.stages.find(stage => stage.number === number)?.title
const accountsById = computed(() => new Map(lists.value.accounts.map(account => [account.id, account])))
const coaches = computed(() => lists.value.accounts.filter(account => account.role === 'coach'))

async function refresh() {
  const [challenges, tasks, ranks, pilots, entries, stages, accounts] = await Promise.all([
    request('/admin/challenges'), request('/admin/tasks'), request('/admin/ranks'), request('/staff/pilots'), request('/admin/catalog'), request('/admin/stages'),
    request('/admin/accounts'),
  ])
  lists.value = { challenges, tasks, ranks, pilots, stages, accounts }
  catalog.value = entries
}
onMounted(async () => { try { await refresh() } catch (reason) { error.value = reason.message } })

function open(kind, item) {
  editing.value = kind
  editingId.value = item?.id ?? null
  selectedPair.value = ''
  formError.value = ''
  selectedImage.value = ''
  const defaults = {
    challenges: { month: month.value, simulator: 'Assetto Corsa', car: '', track: '', rules: '' },
    tasks: { stage: 1, order: 1, title: '', resultType: 'time', instructions: '', normative: '', pointsPrecision: 2 },
    ranks: { order: (lists.value.ranks.at(-1)?.order ?? 0) + 1, title: '', minimumCompletedTasks: 1,
      minimumCompletedStage: null, requiredTaskIds: [] },
    vehicles: { name: '' }, circuits: { name: '', configuration: '' },
    pairs: { simulator: 'Assetto Corsa', vehicleId: '', circuitId: '', rules: '', enabled: true },
    stage: { number: stageNumbers.value[0], title: stageName(stageNumbers.value[0]) ?? '' },
  }
  input.value = kind === 'images' ? { challenge: item } : kind === 'credentials' ? { ...item, password: '' }
    : { ...defaults[kind], ...item }
  if (kind === 'challenges') input.value.month = input.value.month.slice(0, 7)
  dialog.value.showModal()
}

function applyPair() {
  const pair = catalog.value.pairs.find(item => item.id === selectedPair.value)
  if (!pair) return
  Object.assign(input.value, {
    simulator: pair.simulator, car: vehicleName(pair.vehicleId), track: circuitName(pair.circuitId), rules: pair.rules,
  })
}

async function submit() {
  busy.value = true
  formError.value = ''
  try {
    const body = { ...input.value }
    const kind = editing.value
    const id = editingId.value
    if (kind === 'challenges') body.month += '-01'
    if (kind === 'ranks' && body.minimumCompletedStage === '') body.minimumCompletedStage = null
    if (kind === 'tasks' && body.pointsPrecision === '') body.pointsPrecision = null
    if (kind === 'credentials')
      await write('PUT', '/admin/accounts/' + id + '/credentials', { login: body.login, password: body.password || null })
    else if (kind === 'stage')
      await write('PUT', '/admin/stages/' + body.number, { title: body.title })
    else {
      const resource = ['vehicles', 'circuits', 'pairs'].includes(kind) ? 'catalog/' + kind : kind
      await write(id ? 'PUT' : 'POST', '/admin/' + resource + (id ? '/' + id : ''), body)
    }
    dialog.value.close()
    input.value = {}
    notice.value = 'Сохранено.'
    try { await refresh() } catch (reason) { error.value = reason.message }
  } catch (reason) { formError.value = reason.message }
  finally { busy.value = false }
}

async function upload(event, kind) {
  const file = event.target.files?.[0]
  if (!file) return
  busy.value = true
  formError.value = ''
  selectedImage.value = ''
  try {
    await write('PUT', '/admin/challenges/' + input.value.challenge.id + '/images/' + kind, file)
    selectedImage.value = kind === 'car' ? 'Изображение автомобиля сохранено.' : 'Схема трассы сохранена.'
  } catch (reason) { formError.value = reason.message }
  finally { busy.value = false; event.target.value = '' }
}

async function togglePublication(pilot) {
  busy.value = true
  error.value = ''
  notice.value = ''
  try {
    await write('PUT', '/admin/pilots/' + pilot.id + '/publication', { publishName: !pilot.publishName })
    pilot.publishName = !pilot.publishName
    notice.value = pilot.publishName ? 'Имя будет отображаться в публичном зачёте.' : 'Имя скрыто из публичного зачёта и архива.'
  } catch (reason) { error.value = reason.message }
  finally { busy.value = false }
}
</script>

<template>
  <header class="page-heading"><div><div class="eyebrow">Управление секцией</div>
    <h1>Панель администратора</h1><p>Испытания, учебная программа и правила присвоения званий.</p></div></header>
  <div class="tabs" aria-label="Разделы администратора">
    <button v-for="[key, name] in tabs" :key="key" class="quiet" :class="{ active: tab === key }"
      :aria-pressed="tab === key" @click="tab = key; notice = ''">{{ name }}</button>
  </div>
  <p v-if="error" class="error" role="alert">{{ error }}</p>
  <p v-if="notice" class="success" role="status">{{ notice }}</p>

  <section class="panel admin-panel">
    <div class="panel-head"><h2>{{ tabs.find(([key]) => key === tab)?.[1] }}</h2>
      <div class="actions">
        <button v-if="tab === 'tasks' && stageNumbers.length" class="quiet" @click="open('stage')">Назвать этап</button>
        <button class="primary" @click="open(tab)">+ {{ titles[tab] }}</button>
      </div>
    </div>
    <p v-if="!lists[tab].length" class="empty">Записей пока нет.</p>
    <ul v-else class="admin-items">
      <li v-for="item in lists[tab]" :key="item.id" class="admin-row">
        <div v-if="tab === 'challenges'">
          <h3 class="month-title">{{ monthLabel(item.month) }}</h3>
          <p>{{ item.car }} · {{ item.track }}</p><small>{{ item.simulator }}</small>
          <details><summary>Регламент</summary><p class="preserve">{{ item.rules }}</p></details>
        </div>
        <div v-else-if="tab === 'tasks'">
          <h3>{{ item.order }}. {{ item.title }}</h3>
          <p class="preserve">{{ item.instructions }}</p><small>{{ stageName(item.stage) ?? 'Этап ' + item.stage }} · {{ item.normative }}</small>
        </div>
        <div v-else><h3>{{ item.title }}</h3><p>Не менее {{ item.minimumCompletedTasks }} выполненных заданий
          <span v-if="item.minimumCompletedStage"> · Пройден этап {{ item.minimumCompletedStage }}</span></p>
          <small v-if="item.requiredTaskIds?.length">Обязательные: {{ lists.tasks.filter(task => item.requiredTaskIds.includes(task.id)).map(task => task.title).join(', ') }}</small></div>
        <div v-if="tab === 'challenges'" class="actions">
          <button v-if="item.month.slice(0, 7) >= month" class="quiet" @click="open('images', item)">Изображения</button>
          <button v-if="item.canEdit" class="quiet" @click="open(tab, item)">Изменить</button>
          <span class="tag">{{ item.month.slice(0, 7) === month ? 'На платформе' : item.month.slice(0, 7) > month ? 'Запланировано' : 'Завершено' }}</span>
        </div>
        <div v-else class="actions">
          <span v-if="tab === 'tasks'" class="tag">{{ resultTypes[item.resultType] }}</span>
          <button class="quiet" @click="open(tab, item)">{{ tab === 'ranks' ? 'Настроить' : 'Изменить' }}</button>
        </div>
      </li>
    </ul>
  </section>

  <details v-if="tab === 'tasks'" class="panel content-panel">
    <summary>Автомобили, трассы и допустимые пары</summary>
    <div class="catalog-grid">
      <div><h3>Автомобили</h3><ul v-if="catalog.vehicles.length" class="management-list">
        <li v-for="item in catalog.vehicles" :key="item.id" class="heading-actions"><span>{{ item.name }}</span>
          <button class="quiet" :aria-label="'Изменить автомобиль ' + item.name" @click="open('vehicles', item)">Изменить</button>
        </li>
      </ul><button class="quiet" @click="open('vehicles')">+ Автомобиль</button></div>
      <div><h3>Конфигурации трасс</h3><ul v-if="catalog.circuits.length" class="management-list">
        <li v-for="item in catalog.circuits" :key="item.id" class="heading-actions"><span>{{ item.label }}</span>
          <button class="quiet" :aria-label="'Изменить трассу ' + item.label" @click="open('circuits', item)">Изменить</button>
        </li>
      </ul><button class="quiet" @click="open('circuits')">+ Трасса</button></div>
    </div>
    <div class="heading-actions"><h3>Пары для жеребьёвки</h3>
      <button class="quiet" :disabled="!catalog.vehicles.length || !catalog.circuits.length" @click="open('pairs')">+ Пара</button></div>
    <p v-if="!catalog.pairs.some(pair => pair.enabled)" class="muted">Активных пар пока нет. Добавьте автомобиль, трассу и хотя бы одну пару для автоматической смены испытания.</p>
    <ul class="management-list">
      <li v-for="pair in catalog.pairs" :key="pair.id" class="heading-actions">
        <div><strong>{{ vehicleName(pair.vehicleId) }} · {{ circuitName(pair.circuitId) }}</strong>
          <p class="muted">{{ pair.simulator }} · {{ pair.enabled ? 'Участвует' : 'Исключена' }}</p></div>
        <button class="quiet" @click="open('pairs', pair)">Изменить</button>
      </li>
    </ul>
  </details>

  <details class="panel content-panel">
    <summary>Аккаунты учеников и публикация имён</summary>
    <p class="muted">Включайте показ имени и фамилии после оформления согласия на распространение.</p>
    <p v-if="!lists.pilots.length" class="muted">Ученики ещё не добавлены.</p>
    <ul v-else class="management-list">
      <li v-for="pilot in lists.pilots" :key="pilot.id" class="heading-actions">
        <span>{{ pilot.firstName }} {{ pilot.lastName }} <small class="muted">{{ accountsById.get(pilot.id)?.login }}</small></span>
        <div class="actions">
          <button class="quiet" :disabled="busy" @click="togglePublication(pilot)">{{ pilot.publishName ? 'Скрыть имя' : 'Показать имя' }}</button>
          <button v-if="accountsById.has(pilot.id)" class="quiet" :disabled="busy"
            @click="open('credentials', { ...accountsById.get(pilot.id), label: pilot.firstName + ' ' + pilot.lastName })">Изменить логин и пароль</button>
        </div>
      </li>
    </ul>
  </details>
  <details class="panel content-panel">
    <summary>Аккаунты тренеров</summary>
    <p v-if="!coaches.length" class="muted">Тренеров пока нет.</p>
    <ul v-else class="management-list">
      <li v-for="coach in coaches" :key="coach.id" class="heading-actions">
        <span>{{ coach.login }} <small v-if="!coach.active" class="muted">Доступ отключён</small></span>
        <button class="quiet" :disabled="busy" @click="open('credentials', { ...coach, label: coach.login })">Изменить логин и пароль</button>
      </li>
    </ul>
  </details>

  <dialog ref="dialog" class="dialog" aria-labelledby="admin-dialog-title" @cancel.prevent="!busy && dialog.close()" @close="input = {}; editing = ''">
    <form class="form" @submit.prevent="submit">
      <h2 id="admin-dialog-title">{{ (editingId && editTitles[editing]) || titles[editing] }}</h2>
      <template v-if="editing === 'challenges'">
        <label>Месяц <input v-model="input.month" type="month" :min="month" :readonly="!!editingId" required></label>
        <label v-if="catalog.pairs.length">Заполнить из справочника <select v-model="selectedPair" @change="applyPair">
          <option value="">Выберите пару</option>
          <option v-for="pair in catalog.pairs" :key="pair.id" :value="pair.id">{{ vehicleName(pair.vehicleId) }} · {{ circuitName(pair.circuitId) }}</option>
        </select></label>
        <label>Симулятор <input v-model.trim="input.simulator" required maxlength="120"></label>
        <label>Автомобиль <input v-model.trim="input.car" required maxlength="120"></label>
        <label>Трасса и конфигурация <input v-model.trim="input.track" required maxlength="150"></label>
        <label>Регламент <textarea v-model.trim="input.rules" rows="4" required maxlength="5000"></textarea></label>
      </template>
      <template v-else-if="editing === 'tasks'">
        <div class="form-grid">
          <label>Этап <input v-model.number="input.stage" type="number" min="1" max="1000" required></label>
          <label>Порядок <input v-model.number="input.order" type="number" min="1" max="10000" required></label>
        </div>
        <label>Название <input v-model.trim="input.title" required maxlength="150"></label>
        <label>Условия <textarea v-model.trim="input.instructions" rows="4" required maxlength="5000"></textarea></label>
        <label>Тип результата <select v-model="input.resultType"><option v-for="(label, key) in resultTypes" :key="key" :value="key">{{ label }}</option></select></label>
        <label v-if="input.resultType === 'points'">Знаков после запятой <input v-model.number="input.pointsPrecision" type="number" min="0" max="6" placeholder="2"></label>
        <label>Норматив <input v-model.trim="input.normative" required maxlength="1000"></label>
      </template>
      <template v-else-if="editing === 'ranks'">
        <label>Название <input v-model.trim="input.title" required maxlength="100"></label>
        <div class="form-grid">
          <label>Порядок <input v-model.number="input.order" type="number" min="1" max="1000" required></label>
          <label>Минимум заданий <input v-model.number="input.minimumCompletedTasks" type="number" min="0" max="10000" required></label>
        </div>
        <label>Обязательный завершённый этап <select v-model="input.minimumCompletedStage">
          <option :value="null">Любой этап</option>
          <option v-for="stage in stageNumbers" :key="stage" :value="stage">{{ stageName(stage) ?? 'Этап ' + stage }}</option>
        </select></label>
        <fieldset v-if="lists.tasks.length" class="checks">
          <legend>Обязательные задания</legend>
          <label v-for="task in lists.tasks" :key="task.id"><input v-model="input.requiredTaskIds" type="checkbox" :value="task.id"> {{ task.title }}</label>
        </fieldset>
      </template>
      <template v-else-if="editing === 'images'">
        <p class="muted">{{ input.challenge.car }} · {{ input.challenge.track }}</p>
        <label>Автомобиль, PNG до 8 МБ <input type="file" accept="image/png,.png" :disabled="busy" @change="upload($event, 'car')"></label>
        <label>Схема трассы, PNG до 8 МБ <input type="file" accept="image/png,.png" :disabled="busy" @change="upload($event, 'track')"></label>
        <p v-if="selectedImage" class="success" role="status">{{ selectedImage }}</p>
      </template>
      <template v-else-if="editing === 'vehicles' || editing === 'circuits'">
        <label>Название <input v-model.trim="input.name" required maxlength="120"></label>
        <label v-if="editing === 'circuits'">Конфигурация <input v-model.trim="input.configuration" maxlength="100"></label>
      </template>
      <template v-else-if="editing === 'pairs'">
        <label>Симулятор <input v-model.trim="input.simulator" required maxlength="120"></label>
        <label>Автомобиль <select v-model="input.vehicleId" required><option disabled value="">Выберите</option>
          <option v-for="vehicle in catalog.vehicles" :key="vehicle.id" :value="vehicle.id">{{ vehicle.name }}</option>
        </select></label>
        <label>Трасса <select v-model="input.circuitId" required><option disabled value="">Выберите</option>
          <option v-for="track in catalog.circuits" :key="track.id" :value="track.id">{{ track.label }}</option>
        </select></label>
        <label>Регламент <textarea v-model.trim="input.rules" rows="4" required maxlength="5000"></textarea></label>
        <label class="check-line"><input v-model="input.enabled" type="checkbox"> Участвует в жеребьёвке</label>
      </template>
      <template v-else-if="editing === 'credentials'">
        <p>{{ input.label }}</p>
        <label>Логин <input v-model.trim="input.login" autocomplete="off" minlength="3" maxlength="64" required></label>
        <label>Новый пароль <input v-model="input.password" type="password" autocomplete="new-password"
          :minlength="input.role === 'coach' ? 12 : 8" maxlength="128"></label>
        <p class="muted small">Оставьте поле пароля пустым, чтобы сохранить текущий пароль. Смена логина или пароля завершает действующие сеансы.</p>
      </template>
      <template v-else-if="editing === 'stage'">
        <label>Этап <select v-model="input.number" @change="input.title = stageName(input.number) ?? ''">
          <option v-for="number in stageNumbers" :key="number" :value="number">Этап {{ number }}</option>
        </select></label>
        <label>Название этапа <input v-model.trim="input.title" maxlength="100" required></label>
      </template>
      <p v-if="formError" class="error" role="alert">{{ formError }}</p>
      <div class="actions">
        <button type="button" class="quiet" :disabled="busy" @click="dialog.close()">{{ editing === 'images' ? 'Закрыть' : 'Отмена' }}</button>
        <button v-if="editing !== 'images'" class="primary" :disabled="busy">{{ busy ? 'Сохраняем…' : 'Сохранить' }}</button>
      </div>
    </form>
  </dialog>
</template>

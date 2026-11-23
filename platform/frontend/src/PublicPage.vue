<script setup>
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { RouterLink } from 'vue-router'
import { lapTime, monthLabel, request, session } from './api.js'
import ChallengeCard from './ChallengeCard.vue'
import StandingsTable from './StandingsTable.vue'

const props = defineProps({ view: { type: String, required: true }, month: String })
const data = ref(null)
const busy = ref(false)
const error = ref('')
const now = ref(Date.now())
let timer
let sequence = 0
let rollover

onMounted(() => { timer = setInterval(() => { now.value = Date.now() }, 1000) })
onUnmounted(() => { clearInterval(timer); clearTimeout(rollover) })
const showStaffStandings = computed(() => props.view === 'rating' && ['coach', 'admin'].includes(session.user?.role))
watch(() => [props.view, props.month, showStaffStandings.value], load, { immediate: true })

async function load() {
  clearTimeout(rollover)
  const run = ++sequence
  data.value = null
  error.value = ''
  busy.value = false
  if (props.view === 'about') return
  const path = showStaffStandings.value ? '/staff/challenges/current'
    : props.view === 'rating' ? '/public/leaderboard'
    : props.month ? '/public/archive/' + encodeURIComponent(props.month) : '/public/archive'
  busy.value = true
  try {
    const result = await request(path)
    if (run === sequence) data.value = result
  } catch (reason) {
    if (run === sequence && reason.status !== 404) error.value = reason.message
  } finally {
    if (run === sequence) busy.value = false
  }
}

watch(now, (current, previous) => {
  if (props.view !== 'rating' || !data.value?.closesAt) return
  const cutoff = Date.parse(data.value.closesAt)
  if (previous < cutoff && current >= cutoff)
    rollover = setTimeout(() => load(), 16_000)
})

const remaining = computed(() => {
  if (!data.value?.closesAt) return ''
  const minutes = Math.floor((Date.parse(data.value.closesAt) - now.value) / 60000)
  if (minutes < 0) return 'Завершено'
  const pad = value => String(value).padStart(2, '0')
  return `${Math.floor(minutes / 1440)} д ${pad(Math.floor(minutes / 60) % 24)} ч ${pad(minutes % 60)} м`
})
</script>

<template>
  <template v-if="view === 'about'">
    <header class="page-heading">
      <div><div class="eyebrow">Секция виртуального автоспорта</div>
        <h1>Первый круг. Большой путь.</h1>
        <p>Для детей и подростков 7–16 лет · Чебоксары</p></div>
    </header>
    <section class="about-hero">
      <div>
        <h2>Учимся управлять.<br>Растём как пилоты.</h2>
        <p>Траектория, торможение, контроль автомобиля. От первых заездов — к стабильным результатам и новым стартам вместе с тренером.</p>
        <a class="primary" href="https://vk.ru/krilya_simracing" target="_blank" rel="noopener noreferrer">Узнать о занятиях <span aria-hidden="true">↗</span></a>
        <p class="hero-note">Начальная подготовка не нужна</p>
      </div>
      <div class="brand-art"><img src="/assets/krilya-logo.jpg" width="240" height="240" alt="Крылатый конь, герб секции «Крылья»"></div>
    </section>
    <div class="about-facts">
      <span><strong>7–16 лет</strong> Возраст участников</span>
      <span><strong>2 симулятора</strong> Одинаковое оборудование</span>
      <span><strong>С тренером</strong> На каждом этапе</span>
    </div>
    <section class="about-story">
      <div><div class="eyebrow">От навыка к результату</div><h2>У каждого пилота свой темп.</h2>
        <p>На занятиях учимся выбирать траекторию, находить точки торможения и понимать свои ошибки. На платформе виден весь путь: задания, часы практики и личные рекорды.</p>
        <RouterLink to="/" class="text-link">Посмотреть результаты учеников →</RouterLink></div>
      <div class="contact-card"><h2>Приходите знакомиться</h2>
        <p>Чебоксары, проспект Ленина, 39</p><a class="phone" href="tel:+79033898930">+7 (903) 389-89-30</a>
        <p>Расписание и запись — по телефону или в сообщениях сообщества.</p>
        <details><summary>О поддержке проекта</summary>
          <p>В рамках проекта МОО „Крылья“, реализуемого при поддержке Фонда Потанина.</p>
        </details></div>
    </section>
  </template>
  <template v-else>
    <header class="page-heading">
      <div><div class="eyebrow">{{ view === 'rating' ? 'Сезон ' + new Date().getFullYear() : 'ИСТОРИЯ СЕЗОНА' }}</div>
        <h1>{{ view === 'rating' ? 'Таблица лидеров' : month ? monthLabel(month) || 'Архив испытаний' : 'Результаты остаются.' }}</h1>
        <p v-if="view === 'rating'">Равные условия. Лучший круг в зачёт</p>
        <p v-else-if="month"><RouterLink to="/archive" class="text-link">Все испытания</RouterLink></p>
        <p v-else>Закрытые испытания и достижения пилотов.</p></div>
    </header>
    <p v-if="busy" class="panel empty" role="status">Загружаем данные…</p>
    <div v-else-if="error" class="panel content-panel"><p class="error" role="alert">{{ error }}</p>
      <button class="quiet" @click="load">Повторить</button>
    </div>
    <template v-else-if="data?.challenge">
      <ChallengeCard :challenge="data.challenge" />
      <div class="race-meta">
        <span v-if="view === 'rating'">До конца испытания <strong>{{ remaining }}</strong></span>
        <span><strong>{{ data.standings.length }}</strong> пилотов в зачёте</span>
        <details class="race-rules"><summary>Условия испытания</summary>
          <div><h3>Одинаковые настройки</h3><p class="preserve">{{ data.challenge.rules }}</p>
            <p>Период: {{ monthLabel(data.challenge.month) }}. Лучшее время учитывается отдельно для этого испытания.</p></div>
        </details>
      </div>
      <StandingsTable :standings="data.standings" :title="showStaffStandings ? 'Полный зачёт для тренеров' : 'Результаты пилотов'" searchable />
    </template>
    <template v-else-if="Array.isArray(data) && data.length">
      <div class="archive-grid">
        <article v-for="item in data" :key="item.month" class="panel archive-card">
          <span class="tag">{{ monthLabel(item.month).replace(/ г\.$/, '').toUpperCase() }} / ЗАВЕРШЕНО</span>
          <h2>{{ item.car }} × {{ item.track }}</h2><p>{{ item.simulator }} · общий зачёт</p>
          <div class="eyebrow">ЛУЧШИЙ КРУГ</div>
          <p class="archive-time">{{ item.bestLapMilliseconds ? lapTime(item.bestLapMilliseconds) : '—' }}</p>
          <RouterLink class="quiet" :to="'/archive/' + item.month">Посмотреть итоговую таблицу ↗</RouterLink>
        </article>
      </div>
    </template>
    <p v-else class="panel empty">{{ view === 'rating' ? 'Испытание месяца ещё не опубликовано.' : month ? 'Испытание не найдено.' : 'Завершённых испытаний пока нет.' }}</p>
  </template>
</template>

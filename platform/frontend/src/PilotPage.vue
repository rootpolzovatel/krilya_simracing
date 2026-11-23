<script setup>
import { computed, onMounted, ref } from 'vue'
import { lapTime, monthLabel, request } from './api.js'
import TrainingLessons from './TrainingLessons.vue'

const profile = ref(null)
const error = ref('')
const completed = computed(() => profile.value?.tasks.filter(task => task.completed).length ?? 0)
const challenge = ref(null)
onMounted(async () => {
  request('/public/leaderboard').then(result => { challenge.value = result.challenge }).catch(() => {})
  try { profile.value = await request('/pilot/me') }
  catch (reason) { error.value = reason.message }
})
</script>

<template>
  <header class="page-heading"><div><div class="eyebrow">Кабинет пилота</div>
    <h1>{{ profile ? profile.firstName + ' ' + profile.lastName : 'Кабинет пилота' }}</h1>
    <p v-if="profile">Твой прогресс в секции «Крылья».</p></div></header>
  <p v-if="error" class="panel empty error" role="alert">{{ error }}</p>
  <p v-else-if="!profile" class="panel empty" role="status">Загружаем учебный путь…</p>
  <template v-else>
    <div class="stats">
      <div class="stat"><span>Звание</span><strong>{{ profile.rank }}</strong></div>
      <div class="stat"><span>Практика</span><strong>{{ new Intl.NumberFormat('ru', { maximumFractionDigits: 2 }).format(profile.practiceMinutes / 60) }} ч</strong></div>
      <div class="stat"><span>Учебный путь</span><strong>{{ completed }} / {{ profile.tasks.length }}</strong></div>
    </div>
    <div class="columns">
      <section>
        <div class="section-title"><h2>Твой путь пилота</h2>
          <span v-if="profile.stage" class="tag">ЭТАП {{ String(profile.stage).padStart(2, '0') }}<template v-if="profile.stageTitle"> / {{ profile.stageTitle.toUpperCase() }}</template></span>
        </div>
        <div v-if="profile.tasks.length" class="progress lesson-progress" role="progressbar"
          aria-label="Пройденные задания" :aria-valuemax="profile.tasks.length" :aria-valuenow="completed">
          <i :style="{ width: completed / profile.tasks.length * 100 + '%' }"></i>
        </div>
        <TrainingLessons :tasks="profile.tasks" v-slot="{ entry }">
          <RouterLink v-if="['time', 'points', 'place'].includes(entry.task.resultType)" class="text-link"
            :to="{ name: 'task-results', params: { taskId: entry.task.id } }">Таблица результатов →</RouterLink>
          <p v-if="entry.attempts.length">Результат: <strong>{{ entry.attempts[0].result }}</strong></p>
          <details v-if="entry.attempts.length" class="attempt"><summary>История результатов</summary>
            <div v-for="result in entry.attempts" :key="result.id" class="attempt">
              <p><strong>{{ result.result }}</strong> · {{ result.passed === null ? 'Ожидает решения' : result.passed ? 'Выполнено' : 'Не выполнено' }}</p>
              <p class="small muted">{{ result.title }} · Норматив: {{ result.normative }}</p>
              <p class="small muted preserve">{{ result.instructions }}</p>
            </div>
          </details>
        </TrainingLessons>
      </section>
      <section><div class="section-title"><h2>Личный результат</h2></div>
        <div class="panel">
          <div class="rule">
            <template v-if="challenge"><div class="eyebrow">{{ monthLabel(challenge.month) }}</div>
              <h3>{{ challenge.car }}</h3><p>{{ challenge.track }}</p>
            </template>
            <p class="personal-time">{{ profile.bestLapMilliseconds ? lapTime(profile.bestLapMilliseconds) : '—' }}</p>
            <p>Лучший подтверждённый круг</p>
          </div>
          <div class="rule"><h3>Как перейти дальше?</h3>
            <p>Выполни условия задания на занятии. Тренер подтвердит результат. Если нужна дополнительная практика, тренер может открыть следующее задание отдельно.</p>
          </div>
        </div>
      </section>
    </div>
  </template>
</template>

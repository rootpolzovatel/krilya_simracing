<script setup>
import { computed, ref, watch } from 'vue'
import { lapTime, request } from './api.js'

const props = defineProps({ taskId: { type: String, required: true } })
const board = ref(null)
const loading = ref(false)
const error = ref('')
let requestNumber = 0

const resultLabel = computed(() => ({
  time: 'Лучший круг',
  points: 'Лучший результат, очки',
  place: 'Лучшее место в заезде',
})[board.value?.resultType])

async function load() {
  const current = ++requestNumber
  board.value = null
  error.value = ''
  loading.value = true
  try {
    const data = await request('/pilot/tasks/' + encodeURIComponent(props.taskId) + '/standings')
    if (current === requestNumber) board.value = data
  } catch (reason) {
    if (current === requestNumber) error.value = reason.message
  } finally {
    if (current === requestNumber) loading.value = false
  }
}

function formatResult(value) {
  return board.value.resultType === 'time' ? lapTime(Number(value)) : value.replace('.', ',')
}

watch(() => props.taskId, load, { immediate: true })
</script>

<template>
  <header class="page-heading">
    <div>
      <RouterLink class="text-link" to="/pilot">← К заданиям</RouterLink>
      <h1>{{ board?.title ?? 'Таблица результатов' }}</h1>
      <p v-if="board">{{ board.normative }}</p>
    </div>
  </header>
  <p v-if="loading" class="panel empty" role="status">Загружаем результаты…</p>
  <div v-else-if="error" class="panel content-panel">
    <p class="error" role="alert">{{ error }}</p>
    <button type="button" class="quiet" @click="load">Повторить</button>
  </div>
  <section v-else-if="board" class="panel results" aria-labelledby="task-results-title">
    <div class="panel-head"><h2 id="task-results-title">Результаты пилотов</h2></div>
    <div class="table-scroll" tabindex="0" role="region" aria-label="Таблица результатов задания">
      <table>
        <thead><tr><th scope="col">Место</th><th scope="col">Пилот</th><th scope="col">{{ resultLabel }}</th></tr></thead>
        <tbody>
          <tr v-for="(row, index) in board.standings" :key="index" :class="{ first: row.place === 1 }">
            <td class="rank">{{ row.place }}</td>
            <td>{{ row.name }} <strong v-if="row.isSelf">(ты)</strong></td>
            <td class="time">{{ formatResult(row.result) }}</td>
          </tr>
          <tr v-if="!board.standings.length"><td colspan="3" class="empty">Пока нет подтверждённых результатов.</td></tr>
        </tbody>
      </table>
    </div>
  </section>
</template>

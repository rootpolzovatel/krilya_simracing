<script setup>
import { computed, ref } from 'vue'
import { lapTime } from './api.js'
const props = defineProps({
  standings: { type: Array, required: true },
  searchable: Boolean,
  title: { type: String, default: 'Результаты пилотов' },
})
const search = ref('')
const rows = computed(() => {
  const query = search.value.trim().toLocaleLowerCase('ru')
  return props.standings.filter(person =>
    [person.firstName + ' ' + person.lastName, person.lastName + ' ' + person.firstName]
      .some(name => name.toLocaleLowerCase('ru').includes(query)))
})
</script>

<template>
  <section class="panel results" :aria-label="title">
    <div class="panel-head">
      <h2>{{ title }}</h2>
      <input v-if="searchable" v-model="search" class="search" type="search" placeholder="Найти пилота" aria-label="Поиск пилота">
      <slot v-else name="actions" />
    </div>
    <div class="table-scroll" tabindex="0" role="region" aria-label="Таблица результатов">
      <table>
        <thead><tr><th scope="col">МЕСТО</th><th scope="col">ПИЛОТ</th><th scope="col">ЛУЧШИЙ КРУГ</th><th scope="col">ОТРЫВ, С</th></tr></thead>
        <tbody>
          <tr v-for="(row, index) in rows" :key="row.pilotId ?? index" :class="{ first: row.place === 1 }">
            <td class="rank">{{ String(row.place).padStart(2, '0') }}</td>
            <td><span class="avatar" aria-hidden="true">{{ row.firstName[0] }}{{ row.lastName[0] }}</span>{{ row.firstName }} {{ row.lastName }}</td>
            <td class="time">{{ lapTime(row.milliseconds) }}</td>
            <td class="delta">{{ row.place === 1 ? 'Лидер' : '+' + (row.gapMilliseconds / 1000).toFixed(3) }}</td>
          </tr>
          <tr v-if="!rows.length"><td colspan="4" class="empty">{{ standings.length ? 'Пилоты не найдены' : 'В зачёте пока нет результатов.' }}</td></tr>
        </tbody>
      </table>
    </div>
  </section>
</template>

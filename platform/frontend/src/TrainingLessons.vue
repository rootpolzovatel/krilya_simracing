<script setup>
import { computed } from 'vue'

const props = defineProps({ tasks: { type: Array, required: true }, instructor: Boolean })
const current = computed(() => props.tasks.find(entry => !entry.completed)?.task.id)
</script>

<template>
  <div class="panel lesson-list">
    <p v-if="!tasks.length" class="empty">{{ instructor ? 'Заданий на время пока нет.' : 'Задания ещё не добавлены.' }}</p>
    <details v-for="(entry, index) in tasks" :key="entry.task.id" class="lesson"
      :class="{ done: entry.completed, current: entry.task.id === current }" :open="entry.task.id === current">
      <summary>
        <span class="lesson-num" aria-hidden="true">{{ entry.completed ? '✓' : String(index + 1).padStart(2, '0') }}</span>
        <span><strong>{{ entry.task.title }}</strong>
          <small v-if="!instructor">{{ entry.completed ? 'Подтверждено тренером' : entry.task.id === current ? 'Текущее задание' : entry.manuallyOpened ? 'Открыто тренером' : 'Следующий шаг' }}</small>
        </span>
        <span class="lesson-chevron" aria-hidden="true">+</span>
      </summary>
      <div class="lesson-body">
        <p class="preserve">{{ entry.task.instructions }}</p>
        <p class="criterion">{{ entry.task.normative }}</p>
        <slot :entry="entry" />
      </div>
    </details>
  </div>
</template>

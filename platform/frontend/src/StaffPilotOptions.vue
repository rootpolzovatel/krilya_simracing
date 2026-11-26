<script setup>
defineProps({
  current: { type: Array, default: () => [] },
  others: { type: Array, required: true },
})
</script>

<template>
  <optgroup v-if="current.length" label="Сейчас на занятии">
    <option v-for="pilot in current" :key="pilot.id" :value="pilot.id">{{ pilot.firstName }} {{ pilot.lastName }}</option>
  </optgroup>
  <optgroup v-if="current.length && others.length" label="Остальные ученики">
    <option v-for="pilot in others" :key="pilot.id" :value="pilot.id">{{ pilot.firstName }} {{ pilot.lastName }}</option>
  </optgroup>
  <template v-else-if="!current.length">
    <option v-for="pilot in others" :key="pilot.id" :value="pilot.id">{{ pilot.firstName }} {{ pilot.lastName }}</option>
  </template>
</template>

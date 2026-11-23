<script setup>
import { ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { login, session } from './api.js'

const route = useRoute()
const router = useRouter()
const credentials = ref({ login: '', password: '' })
const error = ref('')
const pending = ref(false)

function returnPath() {
  const path = route.query.returnTo
  if (typeof path !== 'string' || !path.startsWith('/') || path.startsWith('//')) return
  if (router.resolve(path).meta.roles) return path
}

async function submit() {
  error.value = ''
  pending.value = true
  try {
    await login(credentials.value)
    const role = session.user?.role
    const fallback = { pilot: '/pilot', coach: '/coach', admin: '/admin' }[role]
    if (!fallback) throw new Error('Не удалось открыть аккаунт.')
    const target = returnPath()
    await router.replace(target && router.resolve(target).meta.roles.includes(role) ? target : fallback)
  } catch (reason) {
    error.value = reason.status === 401 ? 'Проверьте логин и пароль.' : reason.message
  } finally {
    pending.value = false
  }
}
</script>

<template>
  <section v-if="route.path === '/connection'" class="panel content-panel">
    <h1>Нет связи с сервером</h1><p>Проверьте подключение и повторите попытку.</p>
    <button class="primary" @click="router.replace(returnPath() ?? '/')">Повторить</button>
  </section>
  <template v-else>
  <header class="page-heading"><div><h1>Вход</h1><p>Используйте логин и пароль, выданные в секции.</p></div></header>
  <form class="panel form login-form" @submit.prevent="submit">
    <label>Логин <input v-model.trim="credentials.login" autocomplete="username" maxlength="64" required></label>
    <label>Пароль <input v-model="credentials.password" type="password" autocomplete="current-password" maxlength="128" required></label>
    <p v-if="error" class="error" role="alert">{{ error }}</p>
    <button class="primary" :disabled="pending">{{ pending ? 'Входим…' : 'Войти' }}</button>
  </form>
  </template>
</template>

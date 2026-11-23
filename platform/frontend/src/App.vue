<script setup>
import { computed, onMounted, ref } from 'vue'
import { RouterLink, RouterView, useRouter } from 'vue-router'
import { loadSession, logout, session } from './api.js'

const router = useRouter()
const error = ref('')
const menu = computed(() => [
  { to: '/about', label: 'О секции' },
  { to: '/', label: 'Таблица лидеров' },
  { to: '/archive', label: 'Архив испытаний' },
  ...(!session.user ? [{ to: '/login', label: 'Вход' }] : []),
  ...(session.user?.role === 'pilot' ? [{ to: '/pilot', label: 'Кабинет пилота' }] : []),
  ...(['coach', 'admin'].includes(session.user?.role) ? [{ to: '/coach', label: 'Панель тренера' }] : []),
  ...(session.user?.role === 'admin' ? [{ to: '/admin', label: 'Панель администратора' }] : []),
])
onMounted(loadSession)

async function signOut() {
  error.value = ''
  try {
    await logout()
    await router.push('/')
  } catch (reason) {
    error.value = reason.message
  }
}
</script>

<template>
  <a class="skip-link" href="#main">Перейти к содержимому</a>
  <div class="site">
    <aside class="sidebar">
      <RouterLink to="/" class="brand">
        <img src="/assets/krilya-logo.jpg" width="44" height="44" alt="">
        <span class="brand-name">Крылья<small>ДЕТСКИЙ СИМРЕЙСИНГ</small></span>
      </RouterLink>
      <div class="location">ЧЕБОКСАРЫ / СЕКЦИЯ АВТОСПОРТА</div>
      <nav aria-label="Основная навигация">
        <RouterLink v-for="item in menu" :key="item.to" :to="item.to"
          :class="{ active: item.to === '/archive' ? $route.path.startsWith('/archive') : $route.path === item.to }"
          :aria-current="$route.path === item.to || (item.to === '/archive' && $route.path.startsWith('/archive/')) ? 'page' : undefined">
          {{ item.label }}
        </RouterLink>
        <button v-if="session.user" type="button" class="sign-out" @click="signOut">Выйти</button>
      </nav>
      <p v-if="error" class="error" role="alert">{{ error }}</p>
      <div class="side-bottom"><span class="status-dot" aria-hidden="true"></span> Сезон {{ new Date().getFullYear() }}</div>
    </aside>
    <div class="shell">
      <main id="main" tabindex="-1"><RouterView /></main>
      <footer class="site-footer">
        <b>КРЫЛЬЯ</b>
        <div class="footer-contacts">
          <a href="https://vk.ru/krilya_simracing" target="_blank" rel="noopener noreferrer">Сообщество ВКонтакте ↗</a>
          <a href="tel:+79033898930">+7 (903) 389-89-30</a><span>Чебоксары, проспект Ленина, 39</span>
        </div>
      </footer>
    </div>
  </div>
</template>

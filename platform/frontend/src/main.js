import '@fontsource-variable/manrope'
import { createApp } from 'vue'
import { createRouter, createWebHistory } from 'vue-router'
import App from './App.vue'
import PublicPage from './PublicPage.vue'
import PilotPage from './PilotPage.vue'
import TaskStandingsPage from './TaskStandingsPage.vue'
import StaffPage from './StaffPage.vue'
import AdminPage from './AdminPage.vue'
import LoginPage from './LoginPage.vue'
import { loadSession, session } from './api.js'
import './style.css'

const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: '/', component: PublicPage, props: { view: 'rating' } },
    { path: '/about', component: PublicPage, props: { view: 'about' } },
    { path: '/archive/:month?', component: PublicPage, props: route => ({ view: 'archive', month: route.params.month }) },
    { path: '/connection', component: LoginPage },
    { path: '/login', component: LoginPage },
    { path: '/pilot', component: PilotPage, meta: { roles: ['pilot'] } },
    { path: '/pilot/tasks/:taskId/results', name: 'task-results', component: TaskStandingsPage, props: true,
      meta: { roles: ['pilot'] } },
    { path: '/coach', component: StaffPage, meta: { roles: ['coach', 'admin'] } },
    { path: '/admin', component: AdminPage, meta: { roles: ['admin'] } },
    { path: '/:pathMatch(.*)*', redirect: '/' },
  ],
  scrollBehavior: () => ({ top: 0 }),
})

router.beforeEach(async to => {
  if (!to.meta.roles) return
  await loadSession()
  if (session.unavailable) return { path: '/connection', query: { returnTo: to.fullPath } }
  if (!session.user) return { path: '/login', query: { returnTo: to.fullPath } }
  if (!to.meta.roles.includes(session.user.role)) return '/'
})

createApp(App).use(router).mount('#app')

import { reactive } from 'vue'

export const session = reactive({ user: null, unavailable: false })
let sessionRequest
let csrfToken

export class ApiError extends Error {
  constructor(status, message) {
    super(message)
    this.status = status
  }
}

export async function request(path, { method = 'GET', body } = {}) {
  const headers = { Accept: 'application/json' }
  if (method !== 'GET') {
    csrfToken ??= (await request('/auth/csrf')).token
    headers['X-CSRF-Token'] = csrfToken
  }
  const file = body instanceof Blob
  if (body !== undefined) headers['Content-Type'] = file ? 'image/png' : 'application/json'
  const response = await fetch('/api' + path, { method, headers, credentials: 'same-origin',
    body: file || body === undefined ? body : JSON.stringify(body) })
  if (response.status === 204) return null
  const data = await response.json().catch(() => null)
  if (response.ok) return data
  if (response.status === 401 && path !== '/auth/login') {
    session.user = null
    csrfToken = null
  }
  const validation = data?.errors && Object.values(data.errors).flat()[0]
  const defaults = {
    400: 'Проверьте введённые данные.',
    401: 'Войдите в свой аккаунт.',
    403: 'У вас нет доступа к этому действию.',
    404: 'Данные не найдены.',
    409: 'Данные изменились или уже существуют.',
    413: 'Файл слишком большой. Максимальный размер — 8 МБ.',
    429: 'Слишком много попыток. Попробуйте через минуту.',
  }
  throw new ApiError(response.status, data?.message ?? (validation ? defaults[400] :
    defaults[response.status] ?? 'Не удалось получить ответ от сервера.'))
}

export function loadSession() {
  sessionRequest ??= request('/auth/me').then(user => {
    session.user = user
    session.unavailable = false
  }).catch(reason => {
    session.unavailable = reason.status !== 401
    sessionRequest = null
  })
  return sessionRequest
}

export async function login(body) {
  await request('/auth/login', { method: 'POST', body })
  csrfToken = null
  sessionRequest = null
  await loadSession()
}

export async function logout() {
  await request('/auth/logout', { method: 'POST' })
  csrfToken = null
  session.user = null
  sessionRequest = null
}

export const write = (method, path, body) => request(path, { method, body })
export const moscowDate = () => new Intl.DateTimeFormat('sv-SE', { timeZone: 'Europe/Moscow' }).format(new Date())

export function monthLabel(month) {
  const date = new Date(month + 'T12:00:00Z')
  if (Number.isNaN(date.getTime())) return ''
  return new Intl.DateTimeFormat('ru', { month: 'long', year: 'numeric', timeZone: 'UTC' })
    .format(date)
}

export function lapTime(milliseconds) {
  return `${Math.floor(milliseconds / 60000)}:${String(Math.floor(milliseconds / 1000) % 60).padStart(2, '0')}.${String(milliseconds % 1000).padStart(3, '0')}`
}

export function parseLap(value) {
  const match = /^(\d{1,4}):([0-5]\d)\.(\d{3})$/.exec(value.trim())
  if (!match) throw new Error('Введите время круга в формате 1:39.500.')
  return Number(match[1]) * 60000 + Number(match[2]) * 1000 + Number(match[3])
}

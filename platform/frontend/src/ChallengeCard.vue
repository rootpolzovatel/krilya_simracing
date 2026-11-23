<script setup>
import { computed, useId } from 'vue'
import { monthLabel } from './api.js'
const props = defineProps({ challenge: { type: Object, required: true } })
const normalize = name => name.toLowerCase().replace(/[^a-z0-9]/g, '')
const mazda = computed(() => ['mazdamx5', 'mazdamx5nd'].includes(normalize(props.challenge.car)))
const silverstone = computed(() => normalize(props.challenge.track) === 'silverstonenational')
const patternId = 'mapgrid-' + useId()
const contour = 'M360 108 L494 106 Q563 104 576 149 Q601 220 599 421 Q599 435 609 446 Q640 485 615 518 Q600 545 573 541 Q535 541 496 501 L246 239 Q223 210 207 207 Q176 211 174 246 Q174 280 156 296 Q135 314 115 302 Q83 292 100 249 L159 155 Q180 123 194 125 Q211 109 252 110 Z'
</script>

<template>
  <section class="trial-card trial-brand-split live-trial" aria-label="Условия испытания">
    <div class="trial-copy">
      <span class="trial-kicker">{{ monthLabel(challenge.month) }} · {{ challenge.simulator }}</span>
      <h2>{{ challenge.car }}</h2>
      <p>{{ challenge.track }}</p>
      <span class="trial-meta">Лучший круг</span>
    </div>
    <div class="trial-car">
      <img v-if="challenge.carImageUrl" :src="challenge.carImageUrl" :alt="challenge.car">
      <img v-else-if="mazda" class="built-in-car" src="/assets/mazda-mx5-transparent-v2.png" :alt="challenge.car">
      <span v-else class="trial-media-empty">Фото автомобиля</span>
    </div>
    <div class="trial-map">
      <img v-if="challenge.trackImageUrl" :src="challenge.trackImageUrl" :alt="'Схема трассы ' + challenge.track">
      <svg v-else-if="silverstone" class="silverstone-map" viewBox="70 55 590 525" role="img" aria-label="Silverstone National, схема трассы">
        <defs>
          <pattern :id="patternId" width="32" height="32" patternUnits="userSpaceOnUse">
            <path d="M32 0H0V32" fill="none" stroke="currentColor" stroke-opacity=".06" stroke-width="1" />
          </pattern>
        </defs>
        <rect x="70" y="55" width="590" height="525" :fill="'url(#' + patternId + ')'" />
        <path :d="contour" class="map-glow" /><path :d="contour" class="map-line" />
        <path d="M329 109h43" class="map-start" /><circle cx="350" cy="109" r="6" class="map-start-point" />
        <text x="270" y="81" class="map-start-label">START / FINISH</text>
      </svg>
      <span v-else class="trial-media-empty">Карта трассы</span>
    </div>
  </section>
</template>

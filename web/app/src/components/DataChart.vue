<script setup lang="ts">
// The whole of ECharts, every chart type and component, as echarts-for-react has it for React.
import 'echarts';
import type { EChartsOption } from 'echarts';
import VChart from 'vue-echarts';
import { computed } from 'vue';
import { echartsTheme, useChartTheme } from './chart-theme';

interface ChartProps {
  option: EChartsOption;
  label: string;
  height?: number;
}

/** An ECharts chart in the theme's colours, which follows dark mode; label names it for screen readers. */
const { option, label, height = 320 } = defineProps<ChartProps>();

const chartTheme = useChartTheme();
const theme = computed(() => echartsTheme(chartTheme.value));
</script>

<template>
  <div role="img" :aria-label="label">
    <VChart :option="option" :theme="theme" autoresize :style="{ height: `${height}px` }" />
  </div>
</template>

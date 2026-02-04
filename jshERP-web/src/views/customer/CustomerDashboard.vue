<template>
  <div style="margin: 12px 12px 0px;">
    <a-card :bordered="false" style="margin-bottom: 12px">
      <div style="display:flex; gap:12px; align-items:center; flex-wrap: wrap">
        <div>门店</div>
        <a-select v-model="depotId" style="width: 220px" allowClear placeholder="全部门店" @change="reloadAll">
          <a-select-option v-for="d in depots" :key="d.id" :value="d.id">
            {{ d.depotName }}
          </a-select-option>
        </a-select>
        <div>时间</div>
        <a-select v-model="range" style="width: 160px" @change="reloadAll">
          <a-select-option value="today">今日</a-select-option>
          <a-select-option value="month">本月</a-select-option>
        </a-select>
        <a-button @click="reloadAll">刷新</a-button>
      </div>
    </a-card>

    <a-row :gutter="24">
      <a-col :sm="24" :md="12" :xl="6" :style="{ paddingRight: '0px', marginBottom: '12px' }">
        <chart-card :loading="loading" title="收入总额">
          <div class="kpi">￥{{ formatMoney(summary.incomeTotal) }}</div>
        </chart-card>
      </a-col>
      <a-col :sm="24" :md="12" :xl="6" :style="{ paddingRight: '0px', marginBottom: '12px' }">
        <chart-card :loading="loading" title="消费人数">
          <div class="kpi">{{ summary.consumerCount }}</div>
        </chart-card>
      </a-col>
      <a-col :sm="24" :md="12" :xl="6" :style="{ paddingRight: '0px', marginBottom: '12px' }">
        <chart-card :loading="loading" title="会员总数">
          <div class="kpi">{{ summary.memberTotal }}</div>
        </chart-card>
      </a-col>
      <a-col :sm="24" :md="12" :xl="6" :style="{ paddingRight: '0px', marginBottom: '12px' }">
        <chart-card :loading="loading" title="储值余额">
          <div class="kpi">￥{{ formatMoney(summary.storedBalance) }}</div>
        </chart-card>
      </a-col>
    </a-row>

    <a-row :gutter="24">
      <a-col :sm="24" :md="12" :xl="12" :style="{ paddingRight: '0px', marginBottom: '12px' }">
        <a-card :loading="loading" :bordered="false" :body-style="{paddingRight: '5'}">
          <pie title="消费类型占比" :height="chartHeight" :dataSource="consumeTypeData"/>
        </a-card>
      </a-col>
      <a-col :sm="24" :md="12" :xl="12" :style="{ paddingRight: '0px', marginBottom: '12px' }">
        <a-card :loading="loading" :bordered="false" :body-style="{paddingRight: '5'}">
          <line-chart-multid
            title="营业额趋势"
            :height="chartHeight"
            :dataSource="revenueTrendData"
            :fields="['amount']"
            :aliases="[{field:'amount',alias:'营业额'}]"
          />
        </a-card>
      </a-col>
    </a-row>

    <a-row :gutter="24">
      <a-col :sm="24" :md="16" :xl="16" :style="{ paddingRight: '0px', marginBottom: '12px' }">
        <a-card :loading="loading" :bordered="false" title="最近新增会员">
          <a-table
            size="middle"
            bordered
            rowKey="id"
            :columns="recentColumns"
            :dataSource="recentMembers"
            :pagination="false"
          />
        </a-card>
      </a-col>
      <a-col :sm="24" :md="8" :xl="8" :style="{ paddingRight: '0px', marginBottom: '12px' }">
        <a-card :bordered="false" title="快捷入口">
          <div style="display:flex; flex-direction: column; gap: 8px">
            <a @click="go('/system/member')">会员信息</a>
            <a @click="go('/system/customer')">客户信息</a>
            <a @click="go('/customer/report')">顾客报表</a>
            <a @click="go('/cashier/index')">前台收银</a>
          </div>
        </a-card>
      </a-col>
    </a-row>
  </div>
</template>

<script>
import ChartCard from '@/components/ChartCard'
import Pie from '@/components/chart/Pie'
import LineChartMultid from '@/components/chart/LineChartMultid'
import { getAction } from '@/api/manage'

export default {
  name: 'CustomerDashboard',
  components: { ChartCard, Pie, LineChartMultid },
  data() {
    return {
      loading: false,
      range: 'today',
      depotId: undefined,
      depots: [],
      summary: {
        incomeTotal: 0,
        consumerCount: 0,
        memberTotal: 0,
        storedBalance: 0
      },
      consumeTypeData: [],
      revenueTrendData: [],
      recentMembers: [],
      recentColumns: [
        { title: '会员卡号', dataIndex: 'supplier', width: 160 },
        { title: '联系人', dataIndex: 'contacts', width: 120 },
        { title: '手机号码', dataIndex: 'telephone', width: 140 },
        { title: '创建时间', dataIndex: 'createTime', width: 120 },
        { title: '储值余额', dataIndex: 'advanceIn', width: 120 }
      ],
      chartHeight: document.documentElement.clientHeight - 520
    }
  },
  created() {
    this.initDepots()
    this.reloadAll()
  },
  methods: {
    go(path) {
      this.$router.push({ path })
    },
    formatMoney(v) {
      const n = Number(v || 0)
      return n.toFixed(2)
    },
    async initDepots() {
      const res = await getAction('/depot/findDepotByCurrentUser')
      if (res && res.code === 200) {
        this.depots = res.data || []
      } else {
        this.depots = []
      }
    },
    async reloadAll() {
      this.loading = true
      await Promise.all([
        this.loadSummary(),
        this.loadConsumeTypeRatio(),
        this.loadRevenueTrend(),
        this.loadRecentMembers()
      ])
      this.loading = false
    },
    async loadSummary() {
      const res = await getAction('/customer/dashboard/summary', { range: this.range, depotId: this.depotId })
      if (res && res.code === 200) {
        this.summary = res.data || this.summary
      }
    },
    async loadConsumeTypeRatio() {
      const res = await getAction('/customer/dashboard/consumeTypeRatio', { range: this.range, depotId: this.depotId })
      if (res && res.code === 200) {
        const d = res.data || {}
        this.consumeTypeData = [
          { item: '服务', count: Number(d.serviceAmount || 0) },
          { item: '商品', count: Number(d.productAmount || 0) }
        ]
      }
    },
    async loadRevenueTrend() {
      const res = await getAction('/customer/dashboard/revenueTrend', { range: this.range, depotId: this.depotId })
      if (res && res.code === 200) {
        const rows = (res.data && res.data.rows) ? res.data.rows : []
        this.revenueTrendData = rows.map(r => ({
          type: r.d,
          amount: Number(r.amount || 0)
        }))
      }
    },
    async loadRecentMembers() {
      const res = await getAction('/customer/dashboard/recentMembers', { limit: 10 })
      if (res && res.code === 200) {
        this.recentMembers = res.data || []
      } else {
        this.recentMembers = []
      }
    }
  }
}
</script>

<style scoped>
.kpi {
  font-size: 28px;
  line-height: 32px;
  color: rgba(0, 0, 0, .85);
}
</style>


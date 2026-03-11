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
          <a-tooltip title="统计周期内总收入（服务+商品）" slot="action">
            <a-icon type="info-circle-o" />
          </a-tooltip>
          <div class="kpi-content">
            <div class="kpi-value">￥{{ formatMoney(summary.incomeTotal) }}</div>
            <a-icon type="pay-circle" class="kpi-icon" style="color: #1890ff; background: #e6f7ff;" />
          </div>
        </chart-card>
      </a-col>
      <a-col :sm="24" :md="12" :xl="6" :style="{ paddingRight: '0px', marginBottom: '12px' }">
        <chart-card :loading="loading" title="消费人数">
          <a-tooltip title="统计周期内消费去重人数" slot="action">
            <a-icon type="info-circle-o" />
          </a-tooltip>
          <div class="kpi-content">
            <div class="kpi-value">{{ summary.consumerCount }}</div>
            <a-icon type="team" class="kpi-icon" style="color: #722ed1; background: #f9f0ff;" />
          </div>
        </chart-card>
      </a-col>
      <a-col :sm="24" :md="12" :xl="6" :style="{ paddingRight: '0px', marginBottom: '12px' }">
        <chart-card :loading="loading" title="会员总数">
          <a-tooltip title="当前有效会员总数" slot="action">
            <a-icon type="info-circle-o" />
          </a-tooltip>
          <div class="kpi-content">
            <div class="kpi-value">{{ summary.memberTotal }}</div>
            <a-icon type="user" class="kpi-icon" style="color: #52c41a; background: #f6ffed;" />
          </div>
        </chart-card>
      </a-col>
      <a-col :sm="24" :md="12" :xl="6" :style="{ paddingRight: '0px', marginBottom: '12px' }">
        <chart-card :loading="loading" title="储值余额">
          <a-tooltip title="当前所有会员储值余额总计" slot="action">
            <a-icon type="info-circle-o" />
          </a-tooltip>
          <div class="kpi-content">
            <div class="kpi-value">￥{{ formatMoney(summary.storedBalance) }}</div>
            <a-icon type="wallet" class="kpi-icon" style="color: #faad14; background: #fffbe6;" />
          </div>
        </chart-card>
      </a-col>
    </a-row>

    <a-row :gutter="24">
      <a-col :sm="24" :md="24" :xl="8" :style="{ paddingRight: '0px', marginBottom: '12px' }">
        <a-card :loading="loading" :bordered="false" :body-style="{padding: '12px 0 0 0', height: '360px', overflow: 'hidden'}">
          <pie title="消费类型占比" :height="340" :dataSource="consumeTypeData"/>
        </a-card>
      </a-col>
      <a-col :sm="24" :md="24" :xl="16" :style="{ paddingRight: '0px', marginBottom: '12px' }">
        <a-card :loading="loading" :bordered="false" :body-style="{padding: '24px', height: '360px'}">
          <line-chart-multid
            title="营业额趋势"
            :height="310"
            :dataSource="revenueTrendData"
            :fields="['amount']"
            :aliases="[{field:'amount',alias:'营业额'}]"
          />
        </a-card>
      </a-col>
    </a-row>

    <a-row :gutter="24">
      <a-col :sm="24" :md="16" :xl="16" :style="{ paddingRight: '0px', marginBottom: '12px' }">
        <a-card :loading="loading" :bordered="false" title="最近新增会员" style="min-height: 420px;">
          <a-table
            size="small"
            bordered
            rowKey="id"
            :columns="recentColumns"
            :dataSource="recentMembers"
            :pagination="false"
          >
            <template slot="createTime" slot-scope="text">
              {{ formatTime(text) }}
            </template>
          </a-table>
        </a-card>
      </a-col>
      <a-col :sm="24" :md="8" :xl="8" :style="{ paddingRight: '0px', marginBottom: '12px' }">
        <a-card :bordered="false" title="快捷入口" :body-style="{padding: '12px'}" style="min-height: 420px;">
          <div class="quick-nav-grid">
            <div class="quick-nav-item" @click="go('/system/member')">
              <a-icon type="user" class="nav-icon" style="color: #1890ff"/>
              <span>会员信息</span>
            </div>
            <div class="quick-nav-item" @click="go('/system/customer')">
              <a-icon type="team" class="nav-icon" style="color: #52c41a"/>
              <span>客户信息</span>
            </div>
            <div class="quick-nav-item" @click="go('/customer/report')">
              <a-icon type="file-text" class="nav-icon" style="color: #faad14"/>
              <span>顾客报表</span>
            </div>
            <div class="quick-nav-item" @click="go('/cashier/index')">
              <a-icon type="pay-circle" class="nav-icon" style="color: #f5222d"/>
              <span>前台收银</span>
            </div>
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
        { title: '创建时间', dataIndex: 'createTime', width: 140, scopedSlots: { customRender: 'createTime' } },
        { title: '储值余额', dataIndex: 'advanceIn', width: 120 }
      ],
      chartHeight: 300
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
    formatTime(v) {
      if (!v) return ''
      // v is like "2026-02-03T21:37:04.000+0000" or similar
      // Try simple substring if standard format, or Date parse
      if (typeof v === 'string' && v.length > 16) {
        return v.substring(0, 16).replace('T', ' ')
      }
      return v
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
.kpi-content {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding-top: 8px;
}
.kpi-value {
  font-size: 30px;
  line-height: 38px;
  color: rgba(0, 0, 0, .85);
  font-weight: bold;
}
.kpi-icon {
  font-size: 32px;
  padding: 8px;
  border-radius: 50%;
}

.quick-nav-grid {
    display: flex;
    flex-wrap: wrap;
    gap: 16px;
  }
  .quick-nav-item {
    width: calc(50% - 8px);
    height: 90px;
    display: flex;
    flex-direction: column;
    justify-content: center;
    align-items: center;
    background-color: #fafafa;
    border: 1px solid #e8e8e8;
    border-radius: 4px;
    cursor: pointer;
    transition: all 0.3s;
  }
  .quick-nav-item:hover {
    background-color: #fff;
    box-shadow: 0 2px 8px rgba(0,0,0,0.15);
    border-color: #1890ff;
  }
.nav-icon {
  font-size: 24px;
  margin-bottom: 8px;
}
</style>


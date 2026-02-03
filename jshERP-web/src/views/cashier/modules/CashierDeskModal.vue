<template>
  <div ref="container">
    <a-modal
      :visible="visible"
      :footer="null"
      :width="'100%'"
      :getContainer="() => $refs.container"
      :maskClosable="false"
      :closable="false"
      :keyboard="false"
      :bodyStyle="bodyStyle"
      :style="{ top: '0px', paddingBottom: '0px' }"
      wrapClassName="cashier-desk-modal"
      @cancel="handleBack"
    >
      <div class="desk-root">
        <div class="desk-header">
          <a-tabs :activeKey="activeTab" @change="handleTabChange">
            <a-tab-pane key="bill" tab="主单信息" />
            <a-tab-pane key="member" tab="会员刷卡" />
            <a-tab-pane key="service" tab="项目列表" />
            <a-tab-pane key="product" tab="产品列表" />
            <a-tab-pane key="settle" tab="结算" />
          </a-tabs>
          <div class="header-right">
            <a-button type="danger" @click="handleCheckout">结算</a-button>
          </div>
        </div>

        <div class="desk-content">
          <div class="desk-left">
            <div class="left-title">
              <div class="left-seat">{{ seatName }}</div>
              <div class="left-meta">
                <span>会话：{{ sessionId }}</span>
                <span v-if="detail && detail.member">会员：{{ detail.member.name || detail.member.supplier || detail.member.telephone || detail.member.id }}</span>
              </div>
            </div>

            <div class="left-list">
              <a-table
                size="small"
                :columns="columns"
                :dataSource="lineItems"
                :rowKey="rowKey"
                :pagination="false"
              >
                <template slot="qty" slot-scope="text, record">
                  <a-input-number
                    :min="1"
                    :value="record.qty"
                    size="small"
                    @change="v => handleQtyChange(record, v)"
                  />
                </template>
                <template slot="action" slot-scope="text, record">
                  <a-button size="small" type="link" @click="handleRemove(record)">删除</a-button>
                </template>
              </a-table>
            </div>

            <div class="left-footer">
              <div class="total">
                <span>合计：</span>
                <span class="amount">¥ {{ totalAmount }}</span>
              </div>
              <div class="footer-actions">
                <a-button @click="handleReload">刷新</a-button>
                <a-button @click="handleBack">返回房台</a-button>
              </div>
            </div>
          </div>

          <div class="desk-right">
            <div class="right-filter">
              <div class="filter-item">
                <span class="filter-label">{{ activeTab === 'product' ? '产品分类：' : '项目分类：' }}</span>
                <a-select v-model="categoryId" style="width: 180px" :allowClear="true" placeholder="全部">
                  <a-select-option v-for="c in categories" :key="c.id" :value="c.id">
                    {{ c.name }}
                  </a-select-option>
                </a-select>
              </div>
              <div class="filter-item">
                <span class="filter-label">{{ activeTab === 'product' ? '产品查询：' : '项目查询：' }}</span>
                <a-input
                  v-model="keyword"
                  style="width: 260px"
                  placeholder="请输入编号/名称并按回车查询"
                  @pressEnter="handleSearch"
                />
              </div>
              <a-button type="primary" @click="handleSearch">查询</a-button>
            </div>

            <div class="right-grid" v-if="activeTab === 'service'">
              <div v-for="item in serviceItems" :key="item.id" class="grid-item" @click="handleAddService(item)">
                <div class="grid-name">{{ item.name }}</div>
                <div class="grid-sub">¥ {{ formatMoney(item.price) }} / {{ item.durationMin || item.duration_min || '-' }} 分钟</div>
              </div>
            </div>

            <div class="right-grid" v-else-if="activeTab === 'product'">
              <div v-for="item in products" :key="item.id" class="grid-item" @click="handleAddProduct(item)">
                <div class="grid-name">{{ item.name }}</div>
                <div class="grid-sub">¥ {{ formatMoney(item.commodityDecimal || item.purchaseDecimal || item.unitPrice || item.price) }}</div>
              </div>
            </div>

            <div class="right-empty" v-else>
              <div class="empty-title">{{ tabTitle }}</div>
              <div class="empty-sub">该页将在下一阶段补齐更完整的流程</div>
            </div>
          </div>
        </div>
      </div>
    </a-modal>
  </div>
</template>

<script>
import { getAction } from '@/api/manage'
import {
  cashierSessionDetail,
  cashierServiceItemList,
  cashierCartProductAdd,
  cashierCartProductUpdateQty,
  cashierCartProductDelete,
  cashierServiceOrderQuickAddItem,
  cashierServiceOrderItemUpdateQty,
  cashierServiceOrderItemDelete
} from '@/api/api'

export default {
  name: 'CashierDeskModal',
  props: {
    visible: { type: Boolean, default: false },
    seat: { type: Object, default: null },
    session: { type: Object, default: null }
  },
  data() {
    return {
      activeTab: 'service',
      detail: null,
      keyword: '',
      categoryId: undefined,
      categories: [],
      serviceItems: [],
      products: [],
      bodyStyle: {
        padding: '0px',
        height: '100vh'
      },
      columns: [
        { title: '类型', dataIndex: 'type', width: 70 },
        { title: '名称', dataIndex: 'name' },
        { title: '单价', dataIndex: 'unitPrice', width: 90 },
        { title: '数量', key: 'qty', scopedSlots: { customRender: 'qty' }, width: 120 },
        { title: '金额', dataIndex: 'amount', width: 90 },
        { title: '操作', key: 'action', scopedSlots: { customRender: 'action' }, width: 80 }
      ]
    }
  },
  computed: {
    sessionId() {
      return this.session && this.session.id ? this.session.id : ''
    },
    seatName() {
      return this.seat && this.seat.name ? this.seat.name : '未选择座席'
    },
    lineItems() {
      if (!this.detail) return []
      return (this.detail.items || []).map(i => ({
        ...i,
        unitPrice: this.formatMoney(i.unitPrice),
        amount: this.formatMoney(i.amount)
      }))
    },
    totalAmount() {
      if (!this.detail) return '0.00'
      return this.formatMoney(this.detail.totalAmount)
    },
    tabTitle() {
      const map = {
        bill: '主单信息',
        member: '会员刷卡',
        settle: '结算'
      }
      return map[this.activeTab] || ''
    }
  },
  watch: {
    visible(v) {
      if (v) {
        this.bootstrap()
      }
    }
  },
  methods: {
    rowKey(r) {
      return `${r.type}-${r.id}`
    },
    formatMoney(v) {
      const n = Number(v || 0)
      return n.toFixed(2)
    },
    async bootstrap() {
      this.activeTab = 'service'
      this.keyword = ''
      this.categoryId = undefined
      await this.loadDetail()
      await this.loadServiceItems()
    },
    async loadDetail() {
      if (!this.sessionId) return
      const res = await cashierSessionDetail({ sessionId: this.sessionId })
      if (res.code === 200) {
        this.detail = res.data
        return
      }
      this.detail = null
    },
    async loadServiceItems() {
      const res = await cashierServiceItemList({
        currentPage: 1,
        pageSize: 200,
        search: JSON.stringify({ enabled: true, name: this.keyword })
      })
      if (res.code === 200 && res.data) {
        this.serviceItems = res.data.rows || []
        return
      }
      this.serviceItems = []
    },
    async loadProducts() {
      const res = await getAction('/material/list', {
        currentPage: 1,
        pageSize: 200,
        search: JSON.stringify({
          categoryId: this.categoryId,
          materialParam: this.keyword
        })
      })
      if (res.code === 200 && res.data) {
        this.products = res.data.rows || []
        return
      }
      this.products = []
    },
    handleTabChange(key) {
      this.activeTab = key
      if (key === 'service') {
        this.handleSearch()
        return
      }
      if (key === 'product') {
        this.loadProducts()
      }
    },
    async handleSearch() {
      if (this.activeTab === 'service') {
        await this.loadServiceItems()
      }
      if (this.activeTab === 'product') {
        await this.loadProducts()
      }
    },
    async handleAddService(item) {
      if (!this.sessionId) return
      const res = await cashierServiceOrderQuickAddItem({
        sessionId: this.sessionId,
        serviceItemId: item.id,
        qty: 1
      })
      if (res.code === 200) {
        await this.loadDetail()
        return
      }
      this.$message.error(res.data || '添加项目失败')
    },
    async handleAddProduct(item) {
      if (!this.sessionId) return
      const res = await cashierCartProductAdd({
        sessionId: this.sessionId,
        materialId: item.id,
        materialName: item.name,
        unitPrice: item.commodityDecimal,
        qty: 1
      })
      if (res.code === 200) {
        await this.loadDetail()
        return
      }
      this.$message.error(res.data || '添加产品失败')
    },
    async handleQtyChange(record, qty) {
      if (!qty || qty <= 0) return
      if (record.refType === 'SERVICE') {
        const res = await cashierServiceOrderItemUpdateQty({ id: record.id, qty })
        if (res.code === 200) {
          await this.loadDetail()
          return
        }
        this.$message.error(res.data || '修改失败')
        return
      }
      if (record.refType === 'PRODUCT') {
        const res = await cashierCartProductUpdateQty({ id: record.id, qty })
        if (res.code === 200) {
          await this.loadDetail()
          return
        }
        this.$message.error(res.data || '修改失败')
      }
    },
    async handleRemove(record) {
      if (record.refType === 'SERVICE') {
        const res = await cashierServiceOrderItemDelete({ id: record.id })
        if (res.code === 200) {
          await this.loadDetail()
          return
        }
        this.$message.error(res.data || '删除失败')
        return
      }
      if (record.refType === 'PRODUCT') {
        const res = await cashierCartProductDelete({ id: record.id })
        if (res.code === 200) {
          await this.loadDetail()
          return
        }
        this.$message.error(res.data || '删除失败')
      }
    },
    async handleReload() {
      await this.loadDetail()
    },
    handleBack() {
      this.$emit('close')
    },
    async handleCheckout() {
      if (!this.sessionId) return
      this.$emit('checkout', { sessionId: this.sessionId, clearSeat: true })
    }
  }
}
</script>

<style lang="less" scoped>
.desk-root {
  height: 100vh;
  display: flex;
  flex-direction: column;
  background: #f0f2f5;
}

.desk-header {
  display: flex;
  align-items: center;
  background: #fff;
  padding: 0 16px;
  box-shadow: 0 1px 4px rgba(0, 0, 0, 0.08);
}

.header-right {
  margin-left: auto;
}

.desk-content {
  flex: 1;
  display: flex;
  overflow: hidden;
}

.desk-left {
  width: 42%;
  background: #fff;
  display: flex;
  flex-direction: column;
  border-right: 1px solid #e8e8e8;
}

.left-title {
  padding: 12px 16px;
  border-bottom: 1px solid #f0f0f0;
}

.left-seat {
  font-size: 18px;
  font-weight: 600;
}

.left-meta {
  margin-top: 6px;
  color: rgba(0, 0, 0, 0.65);
  display: flex;
  gap: 12px;
  flex-wrap: wrap;
}

.left-list {
  flex: 1;
  overflow: auto;
  padding: 12px 12px 0 12px;
}

.left-footer {
  padding: 12px 16px;
  border-top: 1px solid #f0f0f0;
  display: flex;
  align-items: center;
  justify-content: space-between;
}

.amount {
  font-size: 18px;
  font-weight: 600;
  color: #f5222d;
}

.footer-actions {
  display: flex;
  gap: 8px;
}

.desk-right {
  flex: 1;
  display: flex;
  flex-direction: column;
  padding: 12px;
}

.right-filter {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 8px 12px;
  background: #fff;
  border: 1px solid #f0f0f0;
  border-radius: 4px;
}

.filter-item {
  display: flex;
  align-items: center;
  gap: 6px;
}

.filter-label {
  color: rgba(0, 0, 0, 0.65);
}

.right-grid {
  margin-top: 12px;
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 10px;
  overflow: auto;
  padding-bottom: 12px;
}

.grid-item {
  background: #1890ff;
  color: #fff;
  border-radius: 4px;
  padding: 10px 12px;
  cursor: pointer;
  min-height: 74px;
  display: flex;
  flex-direction: column;
  justify-content: space-between;
}

.grid-name {
  font-weight: 600;
}

.grid-sub {
  font-size: 12px;
  opacity: 0.9;
}

.right-empty {
  margin-top: 12px;
  flex: 1;
  background: #fff;
  border: 1px dashed #d9d9d9;
  border-radius: 4px;
  display: flex;
  flex-direction: column;
  justify-content: center;
  align-items: center;
  color: rgba(0, 0, 0, 0.55);
}

.empty-title {
  font-size: 16px;
  font-weight: 600;
}

.empty-sub {
  margin-top: 6px;
  font-size: 12px;
}
</style>

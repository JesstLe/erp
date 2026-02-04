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
            <a-button type="danger" @click="handleHeaderCheckout">{{ activeTab === 'settle' ? '确认结算' : '结算' }}</a-button>
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
            <div class="right-filter" v-if="activeTab === 'service' || activeTab === 'product'">
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

            <div class="right-panel" v-else-if="activeTab === 'bill'">
              <a-card size="small" :bordered="false">
                <a-descriptions :column="1" size="small" bordered>
                  <a-descriptions-item label="座席">{{ seatName }}</a-descriptions-item>
                  <a-descriptions-item label="会话">{{ sessionId }}</a-descriptions-item>
                  <a-descriptions-item label="开台时间">{{ sessionStartTime }}</a-descriptions-item>
                  <a-descriptions-item label="会员">{{ memberLabel }}</a-descriptions-item>
                </a-descriptions>
                <div style="margin-top: 12px">
                  <a-form layout="vertical">
                    <a-form-item label="备注">
                      <a-input v-model="billRemark" placeholder="请输入主单备注" />
                    </a-form-item>
                    <a-button type="primary" @click="handleSaveBillRemark">保存</a-button>
                  </a-form>
                </div>
              </a-card>
            </div>

            <div class="right-panel" v-else-if="activeTab === 'member'">
              <a-card size="small" :bordered="false">
                <div style="display:flex; gap:8px; align-items:center; margin-bottom: 12px; flex-wrap: wrap">
                  <a-input v-model="memberKeyword" style="width: 260px" placeholder="刷卡/手机号/姓名" @pressEnter="handleMemberSearch" />
                  <a-button type="primary" @click="handleMemberSearch">查询</a-button>
                  <a-button v-if="detail && detail.member" @click="handleUnbindMember">取消绑定</a-button>
                </div>
                <a-table
                  size="small"
                  bordered
                  rowKey="id"
                  :columns="memberColumns"
                  :dataSource="memberList"
                  :pagination="false">
                  <span slot="action" slot-scope="text, record">
                    <a-button type="primary" size="small" @click="handleBindMember(record)">绑定</a-button>
                  </span>
                </a-table>
              </a-card>
            </div>

            <div class="right-panel" v-else-if="activeTab === 'settle'">
              <a-card size="small" :bordered="false">
                <a-descriptions :column="1" size="small" bordered>
                  <a-descriptions-item label="项目金额">¥ {{ formatMoney(settlePreview.serviceTotalAmount) }}</a-descriptions-item>
                  <a-descriptions-item label="产品金额">¥ {{ formatMoney(settlePreview.productTotalAmount) }}</a-descriptions-item>
                  <a-descriptions-item label="合计">¥ {{ formatMoney(settlePreview.totalAmount) }}</a-descriptions-item>
                </a-descriptions>
                <div style="margin-top: 12px">
                  <a-form-model :model="settleForm" layout="vertical">
                    <a-row :gutter="12">
                      <a-col v-for="m in paymentMethods" :key="m.key" :span="12">
                        <a-form-model-item :label="m.label">
                          <a-input-number
                            v-model="settleForm.payments[m.key]"
                            :min="0"
                            :step="0.01"
                            style="width: 100%"
                          />
                        </a-form-model-item>
                      </a-col>
                    </a-row>
                    <a-descriptions :column="1" size="small" bordered>
                      <a-descriptions-item label="实收合计">¥ {{ formatMoney(paySum) }}</a-descriptions-item>
                      <a-descriptions-item label="差额">¥ {{ formatMoney(payDiff) }}</a-descriptions-item>
                      <a-descriptions-item v-if="payChange > 0" label="找零">¥ {{ formatMoney(payChange) }}</a-descriptions-item>
                    </a-descriptions>
                    <div style="margin-top: 12px; display:flex; align-items:center; gap: 12px; flex-wrap: wrap">
                      <div>需要发票</div>
                      <a-switch v-model="settleForm.needInvoice" />
                    </div>
                    <div v-if="settleForm.needInvoice" style="margin-top: 12px">
                      <a-row :gutter="12">
                        <a-col :span="12">
                          <a-form-model-item label="抬头类型">
                            <a-select v-model="settleForm.invoiceInfo.buyerType" style="width: 100%">
                              <a-select-option value="PERSONAL">个人</a-select-option>
                              <a-select-option value="COMPANY">企业</a-select-option>
                            </a-select>
                          </a-form-model-item>
                        </a-col>
                        <a-col :span="12">
                          <a-form-model-item label="抬头名称">
                            <a-input v-model="settleForm.invoiceInfo.buyerName" placeholder="个人/公司名称" />
                          </a-form-model-item>
                        </a-col>
                        <a-col :span="12">
                          <a-form-model-item label="税号(企业必填)">
                            <a-input v-model="settleForm.invoiceInfo.taxNo" placeholder="纳税人识别号" />
                          </a-form-model-item>
                        </a-col>
                        <a-col :span="12">
                          <a-form-model-item label="接收邮箱">
                            <a-input v-model="settleForm.invoiceInfo.email" placeholder="用于接收电子发票" />
                          </a-form-model-item>
                        </a-col>
                        <a-col :span="12">
                          <a-form-model-item label="接收手机号(可选)">
                            <a-input v-model="settleForm.invoiceInfo.phone" placeholder="手机号" />
                          </a-form-model-item>
                        </a-col>
                        <a-col :span="12">
                          <a-form-model-item label="开票内容">
                            <a-input v-model="settleForm.invoiceInfo.invoiceContent" placeholder="默认：服务费" />
                          </a-form-model-item>
                        </a-col>
                      </a-row>
                    </div>
                  </a-form-model>
                </div>
                <div style="margin-top: 12px; display:flex; align-items:center; gap: 12px">
                  <div>结算后清台</div>
                  <a-switch v-model="settleClearSeat" />
                </div>
                <div style="margin-top: 12px">
                  <a-button type="danger" :disabled="!canCheckout" @click="handleCheckout">确认结算</a-button>
                </div>
              </a-card>
            </div>
          </div>
        </div>
      </div>
    </a-modal>
  </div>
</template>

<script>
import { getAction } from '@/api/manage'
import dayjs from 'dayjs'
import {
  cashierSessionDetail,
  cashierSessionUpdate,
  cashierServiceItemList,
  cashierCartProductAdd,
  cashierCartProductUpdateQty,
  cashierCartProductDelete,
  cashierBindMember,
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
      billRemark: '',
      memberKeyword: '',
      memberList: [],
      memberSearchTimer: null,
      memberColumns: [
        { title: '会员卡号', dataIndex: 'supplier', width: 160 },
        { title: '联系人', dataIndex: 'contacts', width: 120 },
        { title: '手机号码', dataIndex: 'telephone', width: 140 },
        { title: '预付款', dataIndex: 'advanceIn', width: 100 },
        { title: '操作', key: 'action', scopedSlots: { customRender: 'action' }, width: 90 }
      ],
      settlePreview: { serviceTotalAmount: 0, productTotalAmount: 0, totalAmount: 0 },
      settleClearSeat: true,
      paymentMethods: [
        { key: 'WECHAT', label: '微信' },
        { key: 'ALIPAY', label: '支付宝' },
        { key: 'CASH', label: '现金' },
        { key: 'BANK', label: '银行卡/POS' },
        { key: 'CARD', label: '储值卡' },
        { key: 'CREDIT', label: '挂账/签单' }
      ],
      settleForm: {
        payments: {
          WECHAT: 0,
          ALIPAY: 0,
          CASH: 0,
          BANK: 0,
          CARD: 0,
          CREDIT: 0
        },
        needInvoice: false,
        invoiceInfo: {
          buyerType: 'PERSONAL',
          buyerName: '',
          taxNo: '',
          email: '',
          phone: '',
          invoiceType: 'ELECTRONIC_NORMAL',
          invoiceContent: '服务费'
        }
      },
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
    sessionStartTime() {
      if (!this.detail || !this.detail.session || !this.detail.session.startTime) return ''
      return dayjs(this.detail.session.startTime).format('YYYY-MM-DD HH:mm:ss')
    },
    memberLabel() {
      if (!this.detail || !this.detail.member) return '未绑定'
      const m = this.detail.member
      return m.supplier || m.contacts || m.telephone || m.id
    },
    tabTitle() {
      const map = {
        bill: '主单信息',
        member: '会员刷卡',
        settle: '结算'
      }
      return map[this.activeTab] || ''
    },
    paySum() {
      const p = (this.settleForm && this.settleForm.payments) ? this.settleForm.payments : {}
      let sum = 0
      Object.keys(p).forEach(k => {
        const v = Number(p[k] || 0)
        if (!isNaN(v)) sum += v
      })
      return sum
    },
    payDiff() {
      const total = Number((this.settlePreview && this.settlePreview.totalAmount) || 0)
      const diff = this.paySum - total
      return diff
    },
    payChange() {
      return this.payDiff > 0 ? this.payDiff : 0
    },
    canCheckout() {
      const total = Number((this.settlePreview && this.settlePreview.totalAmount) || 0)
      if (this.paySum + 1e-9 < total) return false
      const cardPay = Number((this.settleForm && this.settleForm.payments && this.settleForm.payments.CARD) || 0)
      if (cardPay > 0) {
        if (!this.detail || !this.detail.member || !this.detail.member.id) return false
      }
      if (this.settleForm.needInvoice) {
        const info = this.settleForm.invoiceInfo || {}
        const buyerName = (info.buyerName || '').trim()
        const email = (info.email || '').trim()
        if (!buyerName || !email) return false
        if ((info.buyerType || '') === 'COMPANY') {
          const taxNo = (info.taxNo || '').trim()
          if (!taxNo) return false
        }
      }
      return true
    }
  },
  watch: {
    visible(v) {
      if (v) {
        this.bootstrap()
      }
    },
    memberKeyword() {
      this.scheduleMemberSearch()
    }
  },
  methods: {
    setActiveTab(key) {
      this.handleTabChange(key)
    },
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
      this.billRemark = ''
      this.memberKeyword = ''
      this.memberList = []
      this.settlePreview = { serviceTotalAmount: 0, productTotalAmount: 0, totalAmount: 0 }
      this.settleClearSeat = true
      this.settleForm = {
        payments: {
          WECHAT: 0,
          ALIPAY: 0,
          CASH: 0,
          BANK: 0,
          CARD: 0,
          CREDIT: 0
        },
        needInvoice: false,
        invoiceInfo: {
          buyerType: 'PERSONAL',
          buyerName: '',
          taxNo: '',
          email: '',
          phone: '',
          invoiceType: 'ELECTRONIC_NORMAL',
          invoiceContent: '服务费'
        }
      }
      await this.loadDetail()
      await this.loadServiceItems()
    },
    async loadDetail() {
      if (!this.sessionId) return
      const res = await cashierSessionDetail({ sessionId: this.sessionId })
      if (res.code === 200) {
        this.detail = res.data
        this.billRemark = (this.detail && this.detail.session && this.detail.session.remark) ? this.detail.session.remark : ''
        return
      }
      this.detail = null
    },
    async loadSettlePreview() {
      if (!this.sessionId) return
      const res = await getAction('/cashier/settlement/preview', { sessionId: this.sessionId })
      if (res.code === 200) {
        this.settlePreview = res.data || { serviceTotalAmount: 0, productTotalAmount: 0, totalAmount: 0 }
        this.initDefaultPayment()
        return
      }
      this.settlePreview = { serviceTotalAmount: 0, productTotalAmount: 0, totalAmount: 0 }
    },
    initDefaultPayment() {
      const p = (this.settleForm && this.settleForm.payments) ? this.settleForm.payments : {}
      const hasAny = Object.keys(p).some(k => Number(p[k] || 0) > 0)
      if (hasAny) return
      const total = Number((this.settlePreview && this.settlePreview.totalAmount) || 0)
      this.$set(this.settleForm.payments, 'WECHAT', total)
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
      if (this.memberSearchTimer) {
        clearTimeout(this.memberSearchTimer)
        this.memberSearchTimer = null
      }
      this.activeTab = key
      if (key === 'bill') {
        this.loadDetail()
        return
      }
      if (key === 'member') {
        this.memberList = []
        return
      }
      if (key === 'settle') {
        this.loadSettlePreview()
        return
      }
      if (key === 'service') {
        this.handleSearch()
        return
      }
      if (key === 'product') {
        this.loadProducts()
      }
    },
    async handleSaveBillRemark() {
      if (!this.sessionId) return
      const res = await cashierSessionUpdate({ sessionId: this.sessionId, remark: this.billRemark })
      if (res.code === 200) {
        this.$message.success('保存成功')
        await this.loadDetail()
        return
      }
      this.$message.error(res.data || '保存失败')
    },
    scheduleMemberSearch() {
      if (!this.visible) return
      if (this.activeTab !== 'member') return
      if (this.memberSearchTimer) {
        clearTimeout(this.memberSearchTimer)
        this.memberSearchTimer = null
      }
      const key = (this.memberKeyword || '').trim()
      if (!key) {
        this.memberList = []
        return
      }
      this.memberSearchTimer = setTimeout(() => {
        this.handleMemberSearch()
      }, 300)
    },
    async handleMemberSearch() {
      if (this.activeTab !== 'member') return
      const key = (this.memberKeyword || '').trim()
      if (!key) {
        this.memberList = []
        return
      }
      const res = await getAction('/supplier/searchMember', {
        key,
        limit: 20
      })
      if (res.code === 200) {
        this.memberList = res.data || []
        return
      }
      this.memberList = []
    },
    async handleBindMember(member) {
      if (!this.sessionId) return
      const res = await cashierBindMember({ sessionId: this.sessionId, memberId: member.id })
      if (res.code === 200) {
        this.$message.success('绑定成功')
        await this.loadDetail()
        return
      }
      this.$message.error(res.data || '绑定失败')
    },
    async handleUnbindMember() {
      if (!this.sessionId) return
      const res = await cashierBindMember({ sessionId: this.sessionId, memberId: null })
      if (res.code === 200) {
        this.$message.success('已取消绑定')
        await this.loadDetail()
        return
      }
      this.$message.error(res.data || '操作失败')
    },
    handleHeaderCheckout() {
      if (this.activeTab === 'settle') {
        this.handleCheckout()
        return
      }
      this.handleTabChange('settle')
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
      const total = Number((this.settlePreview && this.settlePreview.totalAmount) || 0)
      if (this.paySum + 1e-9 < total) {
        this.$message.warning('实收金额不足')
        return
      }
      const cardPay = Number((this.settleForm && this.settleForm.payments && this.settleForm.payments.CARD) || 0)
      if (cardPay > 0) {
        if (!this.detail || !this.detail.member || !this.detail.member.id) {
          this.$message.warning('储值支付必须先绑定会员')
          return
        }
      }
      if (this.settleForm.needInvoice) {
        const info = this.settleForm.invoiceInfo || {}
        const buyerName = (info.buyerName || '').trim()
        const email = (info.email || '').trim()
        if (!buyerName) {
          this.$message.warning('请填写抬头名称')
          return
        }
        if (!email) {
          this.$message.warning('请填写接收邮箱')
          return
        }
        if ((info.buyerType || '') === 'COMPANY') {
          const taxNo = (info.taxNo || '').trim()
          if (!taxNo) {
            this.$message.warning('企业开票需填写税号')
            return
          }
        }
      }
      const payments = []
      Object.keys(this.settleForm.payments || {}).forEach(k => {
        const v = Number(this.settleForm.payments[k] || 0)
        if (!isNaN(v) && v > 0) {
          payments.push({ payMethod: k, amount: v })
        }
      })
      this.$emit('checkout', {
        sessionId: this.sessionId,
        clearSeat: this.settleClearSeat,
        payments,
        needInvoice: this.settleForm.needInvoice,
        invoiceInfo: this.settleForm.needInvoice ? this.settleForm.invoiceInfo : null
      })
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

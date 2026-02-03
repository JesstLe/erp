<template>
  <div class="cashier-container">
    <!-- 顶部栏 -->
    <div class="top-bar">
      <div class="logo">前台收银</div>
      <div class="store-selector">
        <span>门店：</span>
        <a-select v-model="selectedDepot" style="width: 200px" @change="loadSeats">
          <a-select-option v-for="d in depots" :key="d.id" :value="d.id">
            {{ d.depotName }}
          </a-select-option>
        </a-select>
      </div>
      <div class="actions">
        <a-button icon="reload" @click="loadSeats" style="margin-right: 10px">刷新</a-button>
        <a-button type="danger" @click="$router.push('/')">退出前台</a-button>
      </div>
    </div>

    <!-- 主体内容 -->
    <div class="main-content">
      <div class="seat-grid">
        <div 
          v-for="seat in seats" 
          :key="seat.id" 
          class="seat-card" 
          :class="{ occupied: seat.status === 1 }"
          @click="handleSeatClick(seat)"
        >
          <div class="seat-name">{{ seat.name }}</div>
          <div class="seat-status">{{ seat.status === 1 ? '1/1' : '0/1' }}</div>
        </div>
        <!-- 新增座席 -->
        <div class="seat-card add-seat" @click="handleAddSeat">
          <a-icon type="plus" style="font-size: 24px; margin-bottom: 5px" /> 
          <div>新增座席</div>
        </div>
      </div>
    </div>

    <!-- 底部操作栏 -->
    <div class="bottom-bar">
      <a-button type="primary" class="action-btn" @click="goMember">顾客开卡</a-button>
      <a-button type="primary" class="action-btn" @click="goAdvanceIn">顾客储值</a-button>
      <a-button type="primary" class="action-btn" @click="notImplemented('顾客预约')">顾客预约</a-button>
      <a-button type="primary" class="action-btn" @click="openNursing">顾客护理</a-button>
      <a-button type="primary" class="action-btn" @click="openCredit">签单清账</a-button>
      <a-button type="primary" class="action-btn" @click="goRetailBack">消费退货</a-button>
      <a-button type="primary" class="action-btn" @click="notImplemented('积分增减')">积分增减</a-button>
      <a-button type="primary" class="action-btn" @click="notImplemented('兑换礼品')">兑换礼品</a-button>
      <a-button type="primary" class="action-btn" @click="notImplemented('兑换储值')">兑换储值</a-button>
      <a-button type="primary" class="action-btn" @click="openShift">收银交班</a-button>
    </div>
    
    <!-- 新增座席弹窗 -->
    <a-modal v-model="visible" title="新增座席" @ok="handleOk">
      <a-form-model :model="form">
        <a-form-model-item label="名称">
          <a-input v-model="form.name" placeholder="请输入座席名称，如：修脚 1" />
        </a-form-model-item>
        <a-form-model-item label="排序">
          <a-input v-model="form.sort" placeholder="请输入排序号" />
        </a-form-model-item>
      </a-form-model>
    </a-modal>

    <a-modal v-model="nursingVisible" title="顾客护理" @ok="handleNursingOk">
      <a-form-model :model="nursingForm">
        <a-form-model-item label="技师">
          <a-select v-model="nursingForm.technicianId" placeholder="请选择技师">
            <a-select-option v-for="p in technicians" :key="p.id" :value="p.id">
              {{ p.name }}
            </a-select-option>
          </a-select>
        </a-form-model-item>
        <a-form-model-item label="项目">
          <a-select v-model="nursingForm.serviceItemId" placeholder="请选择项目">
            <a-select-option v-for="i in serviceItems" :key="i.id" :value="i.id">
              {{ i.name }}（{{ i.price }}）
            </a-select-option>
          </a-select>
        </a-form-model-item>
        <a-form-model-item label="数量">
          <a-input-number v-model="nursingForm.qty" :min="1" style="width: 100%" />
        </a-form-model-item>
        <a-form-model-item label="备注">
          <a-input v-model="nursingForm.remark" />
        </a-form-model-item>
        <a-form-model-item>
          <a-checkbox v-model="nursingForm.finishNow">立即完成并计算提成</a-checkbox>
        </a-form-model-item>
      </a-form-model>
    </a-modal>

    <a-modal v-model="creditVisible" title="签单清账" :footer="null">
      <a-tabs v-model="creditTab">
        <a-tab-pane key="create" tab="签单挂账">
          <a-form-model :model="creditForm">
            <a-form-model-item label="挂账金额">
              <a-input-number v-model="creditForm.amount" :min="0" style="width: 100%" />
            </a-form-model-item>
            <a-form-model-item label="备注">
              <a-input v-model="creditForm.remark" />
            </a-form-model-item>
            <a-button type="primary" block @click="handleCreditCreate">确认签单</a-button>
          </a-form-model>
        </a-tab-pane>
        <a-tab-pane key="settle" tab="清账列表">
          <a-table :columns="creditColumns" :dataSource="creditList" :rowKey="r => r.id" :pagination="false">
            <span slot="action" slot-scope="text, record">
              <a-button type="primary" size="small" @click="handleCreditSettle(record)">结清</a-button>
            </span>
          </a-table>
        </a-tab-pane>
      </a-tabs>
    </a-modal>

    <a-modal v-model="shiftVisible" title="收银交班" :footer="null">
      <div v-if="!shift">
        <a-form-model :model="shiftForm">
          <a-form-model-item label="开班备用金">
            <a-input-number v-model="shiftForm.openingAmount" :min="0" style="width: 100%" />
          </a-form-model-item>
          <a-form-model-item label="备注">
            <a-input v-model="shiftForm.remark" />
          </a-form-model-item>
          <a-button type="primary" block @click="handleShiftOpen">开班</a-button>
        </a-form-model>
      </div>
      <div v-else>
        <div style="margin-bottom: 12px">
          <div>开班时间：{{ shift.startTime }}</div>
          <div>备用金：{{ shift.openingAmount }}</div>
        </div>
        <a-form-model :model="shiftForm">
          <a-form-model-item label="交班实收">
            <a-input-number v-model="shiftForm.closingAmount" :min="0" style="width: 100%" />
          </a-form-model-item>
          <a-form-model-item label="备注">
            <a-input v-model="shiftForm.remark" />
          </a-form-model-item>
          <a-button type="primary" block @click="handleShiftHandover">交班</a-button>
        </a-form-model>
      </div>
    </a-modal>
  </div>
</template>

<script>
import { getAction, postAction, putAction } from '@/api/manage'
import {
  cashierOpenSession,
  cashierCloseSession,
  cashierCurrentSessionBySeat,
  cashierServiceItemList,
  cashierServiceOrderCreate,
  cashierServiceOrderAddItem,
  cashierServiceOrderFinish,
  cashierServiceOrderListBySession,
  cashierServiceOrderListItems,
  cashierCreditListOpen,
  cashierCreditCreate,
  cashierCreditSettle,
  cashierShiftCurrent,
  cashierShiftOpen,
  cashierShiftHandover,
  getPersonByType
} from '@/api/api'

export default {
  name: 'Cashier',
  data() {
    return {
      depots: [],
      selectedDepot: undefined,
      seats: [],
      visible: false,
      form: { name: '', sort: '' },
      currentSeat: null,
      currentSession: null,
      nursingVisible: false,
      nursingForm: { technicianId: undefined, serviceItemId: undefined, qty: 1, remark: '', finishNow: true },
      technicians: [],
      serviceItems: [],
      creditVisible: false,
      creditTab: 'create',
      creditForm: { amount: 0, remark: '' },
      creditList: [],
      creditColumns: [
        { title: '单号', dataIndex: 'id', width: 80 },
        { title: '金额', dataIndex: 'amount' },
        { title: '时间', dataIndex: 'createdTime' },
        { title: '备注', dataIndex: 'remark' },
        { title: '操作', key: 'action', scopedSlots: { customRender: 'action' }, width: 90 }
      ],
      shiftVisible: false,
      shift: null,
      shiftForm: { openingAmount: 0, closingAmount: 0, remark: '' }
    }
  },
  created() {
    this.loadDepots()
  },
  methods: {
    loadDepots() {
      getAction('/depot/findDepotByCurrentUser').then(res => {
        if(res.code === 200 && res.data) {
          this.depots = res.data
          if(this.depots.length > 0) {
            this.selectedDepot = this.depots[0].id
            this.loadSeats()
          }
        }
      })
    },
    loadSeats() {
      if(!this.selectedDepot) return
      getAction('/seat/list', { 
        currentPage: 1, 
        pageSize: 100, 
        search: JSON.stringify({ depotId: this.selectedDepot }) 
      }).then(res => {
        if(res.code === 200) {
          this.seats = res.data.rows
        }
      })
    },
    async handleSeatClick(seat) {
      this.currentSeat = seat
      if (seat.status === 1) {
        this.$confirm({
          title: '确认清台？',
          onOk: async () => {
            const currentRes = await cashierCurrentSessionBySeat({ seatId: seat.id })
            if (currentRes.code === 200 && currentRes.data && currentRes.data.id) {
              const closeRes = await cashierCloseSession({ sessionId: currentRes.data.id })
              if (closeRes.code === 200) {
                this.currentSession = null
                this.loadSeats()
                this.$message.success('清台成功')
                return
              }
              this.$message.error('清台失败')
              return
            }
            const fallback = await putAction('/seat/update', { id: seat.id, status: 0 })
            if (fallback.code === 200) {
              this.currentSession = null
              this.loadSeats()
              this.$message.success('清台成功')
              return
            }
            this.$message.error('清台失败')
          }
        })
        return
      }
      const res = await cashierOpenSession({ seatId: seat.id, depotId: this.selectedDepot })
      if (res.code === 200) {
        this.currentSession = res.data
        this.loadSeats()
        this.$message.success('开台成功')
        return
      }
      this.$message.error('开台失败')
    },
    handleAddSeat() {
      this.form = { name: '', sort: '' }
      this.visible = true
    },
    handleOk() {
      if(!this.form.name) {
        this.$message.warning('请输入名称')
        return
      }
      postAction('/seat/add', {
        ...this.form,
        depotId: this.selectedDepot
      }).then(res => {
        if(res.code === 200) {
          this.visible = false
          this.$message.success('添加成功')
          this.loadSeats()
        } else {
          this.$message.error('添加失败')
        }
      })
    },
    goMember() {
      this.$router.push('/system/member')
    },
    goAdvanceIn() {
      this.$router.push('/financial/advance_in')
    },
    goRetailBack() {
      this.$router.push('/bill/retail_back')
    },
    notImplemented(name) {
      this.$message.info(name + '：下一阶段补齐后端与流程')
    },
    async openNursing() {
      if (!this.currentSeat) {
        this.$message.warning('请先选择座席并开台')
        return
      }
      const currentRes = await cashierCurrentSessionBySeat({ seatId: this.currentSeat.id })
      if (currentRes.code !== 200 || !currentRes.data) {
        const openRes = await cashierOpenSession({ seatId: this.currentSeat.id, depotId: this.selectedDepot })
        if (openRes.code !== 200 || !openRes.data) {
          this.$message.warning('请先开台')
          return
        }
        this.currentSession = openRes.data
      } else {
        this.currentSession = currentRes.data
      }
      await this.loadTechnicians()
      await this.loadServiceItems()
      this.nursingForm = { technicianId: undefined, serviceItemId: undefined, qty: 1, remark: '', finishNow: true }
      this.nursingVisible = true
    },
    async loadTechnicians() {
      if (this.technicians && this.technicians.length > 0) {
        return
      }
      const res = await getPersonByType({ type: '技师' })
      if (res.code === 200) {
        const list = res.data && res.data.personList ? res.data.personList : (res.data || [])
        this.technicians = Array.isArray(list) ? list : []
      }
    },
    async loadServiceItems() {
      const res = await cashierServiceItemList({
        currentPage: 1,
        pageSize: 200,
        search: JSON.stringify({ enabled: true })
      })
      if (res.code === 200 && res.data) {
        this.serviceItems = res.data.rows || []
      }
    },
    async handleNursingOk() {
      if (!this.currentSession || !this.currentSession.id) {
        this.$message.warning('请先开台')
        return
      }
      if (!this.nursingForm.technicianId) {
        this.$message.warning('请选择技师')
        return
      }
      if (!this.nursingForm.serviceItemId) {
        this.$message.warning('请选择项目')
        return
      }
      const createRes = await cashierServiceOrderCreate({
        sessionId: this.currentSession.id,
        technicianId: this.nursingForm.technicianId,
        remark: this.nursingForm.remark
      })
      if (createRes.code !== 200 || !createRes.data) {
        this.$message.error('创建护理单失败')
        return
      }
      const orderId = createRes.data.id
      const addRes = await cashierServiceOrderAddItem({
        serviceOrderId: orderId,
        serviceItemId: this.nursingForm.serviceItemId,
        qty: this.nursingForm.qty
      })
      if (addRes.code !== 200) {
        this.$message.error('添加项目失败')
        return
      }
      if (this.nursingForm.finishNow) {
        await cashierServiceOrderFinish({ serviceOrderId: orderId })
      }
      this.nursingVisible = false
      this.$message.success('下单成功')
    },
    async openCredit() {
      if (!this.currentSeat) {
        this.$message.warning('请先选择座席')
        return
      }
      const currentRes = await cashierCurrentSessionBySeat({ seatId: this.currentSeat.id })
      if (currentRes.code !== 200 || !currentRes.data) {
        this.$message.warning('请先开台')
        return
      }
      this.currentSession = currentRes.data
      const totalAmount = await this.calcSessionTotalAmount(this.currentSession.id)
      this.creditForm = { amount: Number(totalAmount), remark: '' }
      await this.loadCreditList()
      this.creditTab = 'create'
      this.creditVisible = true
    },
    async calcSessionTotalAmount(sessionId) {
      const orderRes = await cashierServiceOrderListBySession({ sessionId })
      if (orderRes.code !== 200 || !orderRes.data || !orderRes.data.rows) {
        return 0
      }
      const orders = orderRes.data.rows
      const itemResList = await Promise.all(orders.map(o => cashierServiceOrderListItems({ serviceOrderId: o.id })))
      let sum = 0
      itemResList.forEach(r => {
        if (r.code === 200 && r.data && r.data.rows) {
          r.data.rows.forEach(i => {
            sum += Number(i.amount || 0)
          })
        }
      })
      return sum.toFixed(2)
    },
    async loadCreditList() {
      const res = await cashierCreditListOpen()
      if (res.code === 200 && res.data) {
        this.creditList = res.data.rows || []
      }
    },
    async handleCreditCreate() {
      if (!this.currentSession || !this.currentSession.id) {
        this.$message.warning('请先开台')
        return
      }
      const amount = this.creditForm.amount || 0
      const res = await cashierCreditCreate({
        sessionId: this.currentSession.id,
        memberId: this.currentSession.memberId,
        amount: amount,
        remark: this.creditForm.remark
      })
      if (res.code === 200) {
        this.$message.success('签单成功')
        this.creditTab = 'settle'
        await this.loadCreditList()
        return
      }
      this.$message.error('签单失败')
    },
    async handleCreditSettle(record) {
      const res = await cashierCreditSettle({ id: record.id })
      if (res.code === 200) {
        this.$message.success('清账成功')
        await this.loadCreditList()
        return
      }
      this.$message.error('清账失败')
    },
    async openShift() {
      if (!this.selectedDepot) {
        this.$message.warning('请选择门店')
        return
      }
      const res = await cashierShiftCurrent({ depotId: this.selectedDepot })
      this.shift = res.code === 200 ? res.data : null
      this.shiftForm = { openingAmount: 0, closingAmount: 0, remark: '' }
      this.shiftVisible = true
    },
    async handleShiftOpen() {
      const res = await cashierShiftOpen({
        depotId: this.selectedDepot,
        openingAmount: this.shiftForm.openingAmount,
        remark: this.shiftForm.remark
      })
      if (res.code === 200) {
        this.$message.success('开班成功')
        const cur = await cashierShiftCurrent({ depotId: this.selectedDepot })
        this.shift = cur.code === 200 ? cur.data : null
        return
      }
      this.$message.error('开班失败')
    },
    async handleShiftHandover() {
      const res = await cashierShiftHandover({
        shiftId: this.shift.id,
        closingAmount: this.shiftForm.closingAmount,
        remark: this.shiftForm.remark
      })
      if (res.code === 200) {
        this.$message.success('交班成功')
        this.shiftVisible = false
        this.shift = null
        return
      }
      this.$message.error('交班失败')
    }
  }
}
</script>

<style lang="less" scoped>
.cashier-container {
  display: flex;
  flex-direction: column;
  height: 100vh;
  background: #f0f2f5;
  
  .top-bar {
    height: 60px;
    background: #fff;
    display: flex;
    align-items: center;
    padding: 0 20px;
    box-shadow: 0 1px 4px rgba(0,0,0,0.1);
    z-index: 10;
    
    .logo {
      font-size: 20px;
      font-weight: bold;
      margin-right: 40px;
      color: #1890ff;
    }
    
    .store-selector {
      flex: 1;
    }
  }
  
  .main-content {
    flex: 1;
    padding: 20px;
    overflow-y: auto;
    
    .seat-grid {
      display: flex;
      flex-wrap: wrap;
      gap: 15px;
      
      .seat-card {
        width: 150px;
        height: 100px;
        background: #1890ff;
        color: #fff;
        border-radius: 4px;
        cursor: pointer;
        display: flex;
        flex-direction: column;
        justify-content: center;
        align-items: center;
        transition: all 0.3s;
        box-shadow: 0 2px 8px rgba(0,0,0,0.15);
        
        &.occupied {
          background: #52c41a;
        }
        
        &:hover {
          opacity: 0.9;
          transform: translateY(-2px);
        }
        
        .seat-name {
          font-size: 16px;
          font-weight: bold;
        }
        
        .seat-status {
          margin-top: 5px;
        }
        
        &.add-seat {
          background: #fff;
          color: #999;
          border: 1px dashed #d9d9d9;
          
          &:hover {
            border-color: #1890ff;
            color: #1890ff;
          }
        }
      }
    }
  }
  
  .bottom-bar {
    height: 80px;
    background: #fff;
    border-top: 1px solid #e8e8e8;
    display: flex;
    align-items: center;
    padding: 0 20px;
    gap: 10px;
    overflow-x: auto;
    box-shadow: 0 -1px 4px rgba(0,0,0,0.1);
    z-index: 10;
    
    .action-btn {
      height: 50px;
      min-width: 100px;
      font-size: 16px;
    }
  }
}
</style>

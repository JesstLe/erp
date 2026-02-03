<template>
  <div class="cashier-container">
    <!-- Top Bar -->
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

    <!-- Main Content -->
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
        <!-- Add Seat Button for testing -->
        <div class="seat-card add-seat" @click="handleAddSeat">
          <a-icon type="plus" style="font-size: 24px; margin-bottom: 5px" /> 
          <div>新增座席</div>
        </div>
      </div>
    </div>

    <!-- Bottom Bar -->
    <div class="bottom-bar">
      <a-button type="primary" class="action-btn">顾客开卡</a-button>
      <a-button type="primary" class="action-btn">顾客储值</a-button>
      <a-button type="primary" class="action-btn">顾客预约</a-button>
      <a-button type="primary" class="action-btn">顾客护理</a-button>
      <a-button type="primary" class="action-btn">签单清账</a-button>
      <a-button type="primary" class="action-btn">消费退货</a-button>
      <a-button type="primary" class="action-btn">积分增减</a-button>
      <a-button type="primary" class="action-btn">兑换礼品</a-button>
      <a-button type="primary" class="action-btn">兑换储值</a-button>
      <a-button type="primary" class="action-btn">收银交班</a-button>
    </div>
    
    <!-- Add/Edit Modal -->
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
  </div>
</template>

<script>
import { getAction, postAction, putAction } from '@/api/manage'

export default {
  name: 'Cashier',
  data() {
    return {
      depots: [],
      selectedDepot: undefined,
      seats: [],
      visible: false,
      form: { name: '', sort: '' }
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
    handleSeatClick(seat) {
      // Toggle status for demo
      const newStatus = seat.status === 1 ? 0 : 1
      // Need to construct JSON object for update
      const updateObj = {
        id: seat.id,
        status: newStatus
      }
      putAction('/seat/update', updateObj).then(res => {
        if(res.code === 200) {
          this.loadSeats()
          this.$message.success(newStatus === 1 ? '开台成功' : '清台成功')
        } else {
          this.$message.error('操作失败')
        }
      })
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

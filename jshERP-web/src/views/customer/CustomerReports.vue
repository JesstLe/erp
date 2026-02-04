<template>
  <a-card :bordered="false" :style="cardStyle">
    <div class="table-page-search-wrapper">
      <a-form layout="inline" @keyup.enter.native="handleSearch">
        <a-row :gutter="24">
          <a-col :md="8" :sm="24">
            <a-form-item label="门店" :labelCol="labelCol" :wrapperCol="wrapperCol">
              <a-select v-model="depotId" allowClear placeholder="全部门店">
                <a-select-option v-for="d in depots" :key="d.id" :value="d.id">
                  {{ d.depotName }}
                </a-select-option>
              </a-select>
            </a-form-item>
          </a-col>
          <a-col :md="8" :sm="24">
            <a-form-item label="日期" :labelCol="labelCol" :wrapperCol="wrapperCol">
              <a-range-picker v-model="dateRange" style="width: 100%" />
            </a-form-item>
          </a-col>
          <a-col :md="8" :sm="24">
            <a-form-item label="会员" :labelCol="labelCol" :wrapperCol="wrapperCol">
              <a-input v-model="memberKey" placeholder="卡号/姓名/手机号" />
            </a-form-item>
          </a-col>
        </a-row>
        <a-row :gutter="24">
          <a-col :md="8" :sm="24">
            <a-form-item label="挂账状态" :labelCol="labelCol" :wrapperCol="wrapperCol">
              <a-select v-model="creditStatus" allowClear placeholder="全部">
                <a-select-option value="OPEN">未结清</a-select-option>
                <a-select-option value="SETTLED">已结清</a-select-option>
                <a-select-option value="CANCELED">已取消</a-select-option>
              </a-select>
            </a-form-item>
          </a-col>
          <a-col :md="16" :sm="24">
            <span style="float: left;overflow: hidden;" class="table-page-search-submitButtons">
              <a-button type="primary" @click="handleSearch">查询</a-button>
              <a-button style="margin-left: 8px" @click="handleReset">重置</a-button>
            </span>
          </a-col>
        </a-row>
      </a-form>
    </div>

    <a-tabs v-model="activeTab" @change="handleTabChange">
      <a-tab-pane key="consumption" tab="消费明细" />
      <a-tab-pane key="credit" tab="挂账明细" />
    </a-tabs>

    <a-table
      ref="table"
      size="middle"
      bordered
      rowKey="rowKey"
      :columns="columns"
      :dataSource="dataSource"
      :pagination="ipagination"
      :loading="loading"
      @change="handleTableChange"
    />
  </a-card>
</template>

<script>
import { getAction } from '@/api/manage'

export default {
  name: 'CustomerReports',
  data() {
    return {
      cardStyle: '',
      labelCol: { span: 5 },
      wrapperCol: { span: 18, offset: 1 },
      depots: [],
      depotId: undefined,
      dateRange: [],
      memberKey: '',
      creditStatus: undefined,
      activeTab: 'consumption',
      loading: false,
      dataSource: [],
      ipagination: {
        current: 1,
        pageSize: 10,
        pageSizeOptions: ['10', '20', '30', '50', '100'],
        showTotal: (total, range) => range[0] + '-' + range[1] + ' 共' + total + '条',
        showQuickJumper: true,
        showSizeChanger: true,
        total: 0
      },
      consumptionColumns: [
        { title: '结束时间', dataIndex: 'endTime', width: 170 },
        { title: '门店', dataIndex: 'depotName', width: 120 },
        { title: '会员卡号', dataIndex: 'memberCard', width: 160 },
        { title: '联系人', dataIndex: 'memberName', width: 120 },
        { title: '手机号码', dataIndex: 'memberTelephone', width: 140 },
        { title: '服务金额', dataIndex: 'serviceAmount', width: 120 },
        { title: '商品金额', dataIndex: 'productAmount', width: 120 },
        { title: '合计', dataIndex: 'totalAmount', width: 120 }
      ],
      creditColumns: [
        { title: '创建时间', dataIndex: 'createdTime', width: 170 },
        { title: '状态', dataIndex: 'status', width: 100 },
        { title: '门店', dataIndex: 'depotName', width: 120 },
        { title: '会员卡号', dataIndex: 'memberCard', width: 160 },
        { title: '联系人', dataIndex: 'memberName', width: 120 },
        { title: '手机号码', dataIndex: 'memberTelephone', width: 140 },
        { title: '挂账金额', dataIndex: 'amount', width: 120 }
      ]
    }
  },
  computed: {
    columns() {
      return this.activeTab === 'credit' ? this.creditColumns : this.consumptionColumns
    },
    rowKey() {
      return this.activeTab === 'credit' ? 'id' : 'sessionId'
    }
  },
  created() {
    if (document.documentElement.clientHeight) {
      this.cardStyle = 'height:' + (document.documentElement.clientHeight - 100) + 'px'
    }
    this.initDepots()
    this.loadData()
  },
  methods: {
    async initDepots() {
      const res = await getAction('/depot/findDepotByCurrentUser')
      if (res && res.code === 200) {
        this.depots = res.data || []
      } else {
        this.depots = []
      }
    },
    buildQueryParams() {
      const params = {
        currentPage: this.ipagination.current,
        pageSize: this.ipagination.pageSize,
        depotId: this.depotId,
        memberKey: (this.memberKey || '').trim()
      }
      if (this.dateRange && this.dateRange.length === 2) {
        params.start = this.dateRange[0] ? this.dateRange[0].format('YYYY-MM-DD') : undefined
        params.end = this.dateRange[1] ? this.dateRange[1].add(1, 'day').format('YYYY-MM-DD') : undefined
      }
      if (this.activeTab === 'credit') {
        params.status = this.creditStatus
      }
      return params
    },
    async loadData(arg) {
      if (arg === 1) this.ipagination.current = 1
      this.loading = true
      const url = this.activeTab === 'credit' ? '/customer/report/credit' : '/customer/report/consumption'
      const res = await getAction(url, this.buildQueryParams())
      if (res && res.code === 200 && res.data) {
        this.dataSource = res.data.rows || []
        this.ipagination.total = res.data.total || 0
      } else {
        this.dataSource = []
        this.ipagination.total = 0
      }
      this.loading = false
    },
    handleSearch() {
      this.loadData(1)
    },
    handleReset() {
      this.depotId = undefined
      this.dateRange = []
      this.memberKey = ''
      this.creditStatus = undefined
      this.loadData(1)
    },
    handleTabChange() {
      this.ipagination.current = 1
      this.loadData(1)
    },
    handleTableChange(pagination) {
      this.ipagination = pagination
      this.loadData()
    }
  }
}
</script>


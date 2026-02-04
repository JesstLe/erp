<template>
  <a-row :gutter="24">
    <a-col :md="24">
      <a-card :style="cardStyle" :bordered="false">
        <div class="table-page-search-wrapper">
          <a-form layout="inline" @keyup.enter.native="searchQuery">
            <a-row :gutter="24">
              <a-col :md="6" :sm="24">
                <a-form-item label="名称" :labelCol="labelCol" :wrapperCol="wrapperCol">
                  <a-input placeholder="请输入名称查询" v-model="queryParam.name"></a-input>
                </a-form-item>
              </a-col>
              <a-col :md="6" :sm="24">
                <a-form-item label="状态" :labelCol="labelCol" :wrapperCol="wrapperCol">
                  <a-select v-model="enabledFilter" placeholder="请选择状态">
                    <a-select-option value="">全部</a-select-option>
                    <a-select-option value="true">启用</a-select-option>
                    <a-select-option value="false">禁用</a-select-option>
                  </a-select>
                </a-form-item>
              </a-col>
              <span style="float: left;overflow: hidden;" class="table-page-search-submitButtons">
                <a-col :md="6" :sm="24">
                  <a-button type="primary" @click="searchQuery">查询</a-button>
                  <a-button style="margin-left: 8px" @click="searchReset">重置</a-button>
                </a-col>
              </span>
            </a-row>
          </a-form>
        </div>
        <div class="table-operator" style="margin-top: 5px">
          <a-button v-if="btnEnableList.indexOf(1)>-1" @click="handleAdd" type="primary" icon="plus">新增</a-button>
          <a-button v-if="btnEnableList.indexOf(1)>-1" @click="batchDel" icon="delete">删除</a-button>
          <a-button v-if="btnEnableList.indexOf(1)>-1" @click="batchSetEnabled(true)" icon="check-square">启用</a-button>
          <a-button v-if="btnEnableList.indexOf(1)>-1" @click="batchSetEnabled(false)" icon="close-square">禁用</a-button>
        </div>
        <div>
          <a-table
            ref="table"
            size="middle"
            bordered
            rowKey="id"
            :columns="columns"
            :dataSource="dataSource"
            :pagination="ipagination"
            :scroll="scroll"
            :loading="loading"
            :rowSelection="{selectedRowKeys: selectedRowKeys, onChange: onSelectChange}"
            @change="handleTableChange">
            <span slot="action" slot-scope="text, record">
              <a @click="handleEdit(record)">编辑</a>
              <a-divider v-if="btnEnableList.indexOf(1)>-1" type="vertical" />
              <a-popconfirm v-if="btnEnableList.indexOf(1)>-1" title="确定删除吗?" @confirm="() => handleDelete(record.id)">
                <a>删除</a>
              </a-popconfirm>
            </span>
            <template slot="customRenderEnabled" slot-scope="enabled">
              <a-tag v-if="enabled" color="green">启用</a-tag>
              <a-tag v-else color="orange">禁用</a-tag>
            </template>
          </a-table>
        </div>
        <service-item-modal ref="modalForm" @ok="modalFormOk"></service-item-modal>
      </a-card>
    </a-col>
  </a-row>
</template>

<script>
  import ServiceItemModal from './modules/ServiceItemModal'
  import { JeecgListMixin } from '@/mixins/JeecgListMixin'
  import { cashierUpdateServiceItem } from '@/api/api'
  import { filterObj } from '@/utils/util'

  export default {
    name: 'ServiceItemList',
    mixins: [JeecgListMixin],
    components: {
      ServiceItemModal
    },
    data() {
      return {
        labelCol: { span: 5 },
        wrapperCol: { span: 18, offset: 1 },
        queryParam: { name: '' },
        enabledFilter: '',
        urlPath: '/cashier/service_item',
        columns: [
          {
            title: '#',
            dataIndex: '',
            key: 'rowIndex',
            width: 40,
            align: 'center',
            customRender: function (t, r, index) {
              return parseInt(index) + 1
            }
          },
          {
            title: '操作',
            dataIndex: 'action',
            width: 100,
            align: 'center',
            scopedSlots: { customRender: 'action' }
          },
          { title: '名称', dataIndex: 'name', width: 200 },
          { title: '单价', dataIndex: 'price', width: 100 },
          { title: '时长(分钟)', dataIndex: 'durationMin', width: 100 },
          { title: '排序', dataIndex: 'sort', width: 80 },
          { title: '状态', dataIndex: 'enabled', width: 80, align: 'center', scopedSlots: { customRender: 'customRenderEnabled' } }
        ],
        url: {
          list: '/cashier/serviceItem/list',
          delete: '/cashier/serviceItem/delete',
          deleteBatch: '/cashier/serviceItem/deleteBatch'
        }
      }
    },
    methods: {
      getQueryParams() {
        let sqp = {}
        if (this.superQueryParams) {
          sqp['superQueryParams'] = encodeURI(this.superQueryParams)
          sqp['superQueryMatchType'] = this.superQueryMatchType
        }
        let searchObj = Object.assign({}, this.queryParam)
        if (this.enabledFilter !== '') {
          searchObj.enabled = this.enabledFilter === 'true'
        }
        let param = Object.assign(sqp, { search: JSON.stringify(searchObj) }, this.isorter, this.filters)
        param.field = this.getQueryField()
        param.currentPage = this.ipagination.current
        param.pageSize = this.ipagination.pageSize
        return filterObj(param)
      },
      searchReset() {
        this.queryParam = { name: '' }
        this.enabledFilter = ''
        this.loadData(1)
      },
      async batchSetEnabled(enabled) {
        if (this.selectedRowKeys.length <= 0) {
          this.$message.warning('请选择一条记录！')
          return
        }
        this.loading = true
        try {
          for (const id of this.selectedRowKeys) {
            await cashierUpdateServiceItem({ id, enabled })
          }
          this.$message.success('操作成功')
          this.loadData()
        } finally {
          this.loading = false
        }
      },
      handleEdit(record) {
        this.$refs.modalForm.edit(record)
        this.$refs.modalForm.title = '编辑'
        this.$refs.modalForm.disableSubmit = false
        if (this.btnEnableList.indexOf(1) === -1) {
          this.$refs.modalForm.isReadOnly = true
        }
      }
    }
  }
</script>

<style scoped>
  @import '~@assets/less/common.less'
</style>

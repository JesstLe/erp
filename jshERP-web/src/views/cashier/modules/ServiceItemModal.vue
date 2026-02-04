<template>
  <div ref="container">
    <a-modal
      :title="title"
      :width="800"
      :visible="visible"
      :confirmLoading="confirmLoading"
      :getContainer="() => $refs.container"
      :maskStyle="{'top':'93px','left':'154px'}"
      :wrapClassName="wrapClassNameInfo()"
      :mask="isDesktop()"
      :maskClosable="false"
      @ok="handleOk"
      @cancel="handleCancel"
      cancelText="取消"
      okText="保存"
      style="top:20%;height: 50%;">
      <template slot="footer">
        <a-button key="back" v-if="isReadOnly" @click="handleCancel">取消</a-button>
      </template>
      <a-spin :spinning="confirmLoading">
        <a-form :form="form" id="serviceItemModal">
          <a-form-item :labelCol="labelCol" :wrapperCol="wrapperCol" label="名称">
            <a-input placeholder="请输入名称" v-decorator.trim="['name', validatorRules.name]" :disabled="isReadOnly" />
          </a-form-item>
          <a-form-item :labelCol="labelCol" :wrapperCol="wrapperCol" label="单价">
            <a-input-number
              style="width: 100%"
              :min="0"
              :precision="2"
              placeholder="请输入单价"
              v-decorator="['price', validatorRules.price]"
              :disabled="isReadOnly" />
          </a-form-item>
          <a-form-item :labelCol="labelCol" :wrapperCol="wrapperCol" label="时长(分钟)">
            <a-input-number
              style="width: 100%"
              :min="1"
              :precision="0"
              placeholder="请输入时长"
              v-decorator="['durationMin', validatorRules.durationMin]"
              :disabled="isReadOnly" />
          </a-form-item>
          <a-form-item :labelCol="labelCol" :wrapperCol="wrapperCol" label="排序">
            <a-input placeholder="请输入排序" v-decorator.trim="['sort']" :disabled="isReadOnly" />
          </a-form-item>
          <a-form-item :labelCol="labelCol" :wrapperCol="wrapperCol" label="状态">
            <a-switch
              checked-children="启用"
              un-checked-children="禁用"
              v-decorator="['enabled', { valuePropName: 'checked' }]"
              :disabled="isReadOnly" />
          </a-form-item>
        </a-form>
      </a-spin>
    </a-modal>
  </div>
</template>

<script>
  import pick from 'lodash.pick'
  import { cashierAddServiceItem, cashierUpdateServiceItem } from '@/api/api'
  import { autoJumpNextInput } from '@/utils/util'
  import { mixinDevice } from '@/utils/mixin'

  export default {
    name: 'ServiceItemModal',
    mixins: [mixinDevice],
    data() {
      return {
        title: '操作',
        visible: false,
        model: {},
        isReadOnly: false,
        labelCol: {
          xs: { span: 24 },
          sm: { span: 5 }
        },
        wrapperCol: {
          xs: { span: 24 },
          sm: { span: 16 }
        },
        confirmLoading: false,
        form: this.$form.createForm(this),
        validatorRules: {
          name: {
            rules: [
              { required: true, message: '请输入名称!' },
              { min: 2, max: 30, message: '长度在 2 到 30 个字符', trigger: 'blur' }
            ]
          },
          price: {
            rules: [
              { required: true, message: '请输入单价!' }
            ]
          },
          durationMin: {
            rules: [
              { required: true, message: '请输入时长!' }
            ]
          }
        }
      }
    },
    methods: {
      add() {
        this.edit({})
      },
      edit(record) {
        this.form.resetFields()
        this.model = Object.assign({}, record)
        this.visible = true
        this.$nextTick(() => {
          this.form.setFieldsValue(pick(this.model, 'name', 'price', 'durationMin', 'sort', 'enabled'))
          if (this.model.enabled === undefined || this.model.enabled === null) {
            this.form.setFieldsValue({ enabled: true })
          }
          autoJumpNextInput('serviceItemModal')
        })
      },
      close() {
        this.$emit('close')
        this.visible = false
        this.isReadOnly = false
      },
      handleOk() {
        if (this.isReadOnly) {
          this.close()
          return
        }
        const that = this
        this.form.validateFields((err, values) => {
          if (!err) {
            that.confirmLoading = true
            let formData = Object.assign(this.model, values)
            let obj
            if (!this.model.id) {
              obj = cashierAddServiceItem(formData)
            } else {
              obj = cashierUpdateServiceItem(formData)
            }
            obj.then((res) => {
              if (res.code === 200) {
                that.$emit('ok')
                that.confirmLoading = false
                that.close()
              } else {
                that.$message.warning(res.data || '保存失败')
                that.confirmLoading = false
              }
            })
          }
        })
      },
      handleCancel() {
        this.close()
      }
    }
  }
</script>

<style scoped>
</style>


import { CheckCircleOutlined } from '@ant-design/icons'
import { Alert, Button, Card, Checkbox, Form, Input, Space, Typography } from 'antd'
import { Link } from 'react-router-dom'
import { useState } from 'react'
import { apiRequest, ApiError } from '../api/client'
import type { MerchantRegistrationReceipt } from '../api/types'
import { BrandLogo } from '../components/BrandLogo'

interface RegistrationValues {
  merchantName: string; storeName: string; contactName: string; contactMobile: string
  contactEmail?: string; desiredOwnerAccount: string; note?: string; acceptedTerms: boolean
}

export function MerchantRegisterPage() {
  const [receipt, setReceipt] = useState<MerchantRegistrationReceipt>()
  const [error, setError] = useState<string>()
  const [submitting, setSubmitting] = useState(false)
  const submit = async (values: RegistrationValues) => {
    setSubmitting(true); setError(undefined)
    try { setReceipt(await apiRequest('/api/v1/public/merchant-registration-applications', { method: 'POST', body: JSON.stringify(values) })) }
    catch (requestError) { setError(requestError instanceof ApiError ? requestError.message : '申请提交失败，请稍后重试') }
    finally { setSubmitting(false) }
  }
  return <main className="public-shell">
    <Card className="public-card" variant="borderless">
      <Space orientation="vertical" size={4} className="login-heading"><BrandLogo className="public-brand-logo" /><Typography.Title level={2}>申请开通商户</Typography.Title><Typography.Paragraph type="secondary">提交后由平台管理员审核，不会立即创建登录账号。</Typography.Paragraph></Space>
      {receipt ? <div className="registration-success"><CheckCircleOutlined /><Typography.Title level={3}>申请已提交</Typography.Title><Typography.Paragraph>申请编号：<Typography.Text copyable strong>{receipt.applicationNo}</Typography.Text></Typography.Paragraph><Typography.Paragraph type="secondary">当前状态：待审核。平台管理员审核通过后会线下交付负责人初始密码。</Typography.Paragraph><Link to="/login">返回登录</Link></div> : <>
        {error && <Alert type="error" showIcon title={error} className="login-alert" />}
        <Form<RegistrationValues> layout="vertical" onFinish={submit} requiredMark="optional">
          <Form.Item name="merchantName" label="商户/品牌名称" rules={[{ required: true }, { min: 2, max: 100 }]}><Input /></Form.Item>
          <Form.Item name="storeName" label="首店名称" rules={[{ required: true }, { min: 2, max: 100 }]}><Input /></Form.Item>
          <Form.Item name="contactName" label="联系人姓名" rules={[{ required: true }, { min: 2, max: 60 }]}><Input /></Form.Item>
          <Form.Item name="contactMobile" label="联系手机号" rules={[{ required: true }, { pattern: /^1[3-9]\d{9}$/, message: '请输入有效的11位手机号' }]}><Input maxLength={11} /></Form.Item>
          <Form.Item name="contactEmail" label="联系邮箱"><Input type="email" maxLength={254} /></Form.Item>
          <Form.Item name="desiredOwnerAccount" label="期望负责人账号" extra="4–100位，可使用字母、数字、点、下划线、@和连字符" rules={[{ required: true }, { pattern: /^[A-Za-z0-9._@-]{4,100}$/, message: '账号格式不正确' }]}><Input /></Form.Item>
          <Form.Item name="note" label="申请说明"><Input.TextArea maxLength={500} showCount rows={3} /></Form.Item>
          <Form.Item name="acceptedTerms" valuePropName="checked" rules={[{ validator: (_, value) => value ? Promise.resolve() : Promise.reject(new Error('请先同意服务及隐私条款')) }]}><Checkbox>我已阅读并同意服务及隐私条款</Checkbox></Form.Item>
          <Button type="primary" htmlType="submit" block size="large" loading={submitting}>提交审核</Button>
        </Form>
        <Typography.Paragraph className="public-footer"><Link to="/login">已有账号，返回登录</Link></Typography.Paragraph>
      </>}
    </Card>
  </main>
}

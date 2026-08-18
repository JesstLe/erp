import { SafetyCertificateOutlined } from '@ant-design/icons'
import { Alert, Button, Card, Checkbox, Form, Input, Space, Typography } from 'antd'
import { Link, useNavigate } from 'react-router-dom'
import { useState } from 'react'
import { apiRequest, ApiError, resetCsrfToken } from '../api/client'
import type { PlatformCurrentUser } from '../api/types'

interface Values { account: string; password: string; rememberMe: boolean }

export function PlatformLoginPage() {
  const navigate = useNavigate(); const [error, setError] = useState<string>(); const [loading, setLoading] = useState(false)
  const submit = async (values: Values) => {
    setLoading(true); setError(undefined)
    try {
      const user = await apiRequest<PlatformCurrentUser>('/api/v1/platform/auth/login', { method: 'POST', body: JSON.stringify(values) })
      resetCsrfToken(); navigate(user.mustChangePassword ? '/platform/change-password' : '/platform', { replace: true })
    } catch (requestError) { setError(requestError instanceof ApiError ? requestError.message : '登录失败，请稍后重试') }
    finally { setLoading(false) }
  }
  return <main className="platform-login-shell"><Card className="login-card" variant="borderless">
    <Space orientation="vertical" size={4} className="login-heading"><SafetyCertificateOutlined className="public-icon" /><Typography.Text type="secondary">ERP 平台控制面</Typography.Text><Typography.Title level={2}>平台管理员登录</Typography.Title><Typography.Paragraph type="secondary">该账号独立于所有商户 OWNER。</Typography.Paragraph></Space>
    {error && <Alert type="error" showIcon title={error} className="login-alert" />}
    <Form<Values> layout="vertical" size="large" initialValues={{ rememberMe: false }} onFinish={submit} requiredMark={false}>
      <Form.Item name="account" label="平台账号" rules={[{ required: true }]}><Input autoComplete="username" /></Form.Item>
      <Form.Item name="password" label="密码" rules={[{ required: true }]}><Input.Password autoComplete="current-password" /></Form.Item>
      <Form.Item name="rememberMe" valuePropName="checked"><Checkbox>在受控设备保持登录</Checkbox></Form.Item>
      <Button type="primary" htmlType="submit" block loading={loading}>进入平台管理中心</Button>
    </Form><Typography.Paragraph className="public-footer"><Link to="/login">返回商户登录</Link></Typography.Paragraph>
  </Card></main>
}

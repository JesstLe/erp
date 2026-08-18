import { LockOutlined, ShopOutlined, UserOutlined } from '@ant-design/icons'
import { Alert, Button, Card, Checkbox, Form, Input, Space, Typography } from 'antd'
import { Link, Navigate, useLocation, useNavigate } from 'react-router-dom'
import { useState } from 'react'
import { apiRequest, ApiError, resetCsrfToken } from '../api/client'
import type { CurrentUser } from '../api/types'
import { useAuth } from '../auth/useAuth'

interface LoginValues { account: string; password: string; rememberMe: boolean }

export function LoginPage() {
  const auth = useAuth()
  const navigate = useNavigate()
  const location = useLocation()
  const [error, setError] = useState<string>()
  const [submitting, setSubmitting] = useState(false)
  if (auth.user) return <Navigate to="/" replace />

  const submit = async (values: LoginValues) => {
    setSubmitting(true); setError(undefined)
    try {
      await apiRequest<CurrentUser>('/api/v1/auth/login', { method: 'POST', body: JSON.stringify(values) })
      resetCsrfToken()
      await auth.refresh()
      navigate((location.state as { from?: string } | undefined)?.from ?? '/', { replace: true })
    } catch (requestError) {
      setError(requestError instanceof ApiError ? requestError.message : '登录失败，请稍后重试')
    } finally { setSubmitting(false) }
  }

  return <main className="login-shell">
    <section className="login-brand-panel">
      <div className="brand-mark"><ShopOutlined /></div>
      <Typography.Title level={1}>把门店经营，放在一个清楚的工作台里</Typography.Title>
      <p className="login-brand-copy">从设施接待、服务录单到会员和交班，每个动作都有状态、权限和审计记录。</p>
      <div className="login-stat-row">
        <div><strong>01</strong><span>设施计时独立记录</span></div>
        <div><strong>02</strong><span>价格版本统一管理</span></div>
        <div><strong>03</strong><span>资金流水全程可追溯</span></div>
      </div>
    </section>
    <Card className="login-card" variant="borderless">
      <Space orientation="vertical" size={4} className="login-heading"><Typography.Text type="secondary">门店 ERP</Typography.Text><Typography.Title level={2}>欢迎回来</Typography.Title><Typography.Paragraph type="secondary">请使用员工账号登录系统</Typography.Paragraph></Space>
      {error && <Alert type="error" showIcon title={error} className="login-alert" />}
      <Form<LoginValues> layout="vertical" size="large" initialValues={{ rememberMe: false }} onFinish={submit} requiredMark={false}>
        <Form.Item name="account" label="员工账号" rules={[{ required: true, message: '请输入员工账号' }, { max: 100 }]}><Input prefix={<UserOutlined />} autoComplete="username" placeholder="请输入账号" /></Form.Item>
        <Form.Item name="password" label="密码" rules={[{ required: true, message: '请输入密码' }, { max: 256 }]}><Input.Password prefix={<LockOutlined />} autoComplete="current-password" placeholder="请输入密码" /></Form.Item>
        <Form.Item name="rememberMe" valuePropName="checked"><Checkbox>在此设备保持登录</Checkbox></Form.Item>
        <Button type="primary" htmlType="submit" block loading={submitting}>进入工作台</Button>
      </Form>
      <Typography.Paragraph type="secondary" className="login-security-note">登录失败不会透露账号是否存在；连续失败将暂时锁定账号。</Typography.Paragraph>
      <Typography.Paragraph className="public-footer"><Link to="/register">还没有商户账号？申请开通</Link></Typography.Paragraph>
    </Card>
  </main>
}

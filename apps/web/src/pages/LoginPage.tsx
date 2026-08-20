import { LockOutlined, UserOutlined } from '@ant-design/icons'
import { Alert, Button, Checkbox, Form, Input, Modal, Typography } from 'antd'
import { Link, Navigate, useLocation, useNavigate } from 'react-router-dom'
import { useState } from 'react'
import { apiRequest, ApiError, resetCsrfToken } from '../api/client'
import type { CurrentUser } from '../api/types'
import { useAuth } from '../auth/useAuth'
import { BrandLogo } from '../components/BrandLogo'

interface LoginValues { account: string; password: string; rememberMe: boolean }

export function LoginPage() {
  const auth = useAuth()
  const navigate = useNavigate()
  const location = useLocation()
  const [error, setError] = useState<string>()
  const [submitting, setSubmitting] = useState(false)
  const [recoveryOpen, setRecoveryOpen] = useState(false)
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

  return <main className="login-shell login-shell-refined">
    <section className="login-art-panel" aria-hidden="true">
      <img src="/assets/login-aurora.png" alt="" />
    </section>
    <section className="login-form-panel">
      <div className="login-form-wrap">
        <div className="login-lockup"><BrandLogo label="门店 ERP" /><strong>门店 ERP</strong></div>
        <header className="login-heading">
          <Typography.Title level={1}>欢迎回来</Typography.Title>
          <Typography.Paragraph>登录后进入你的门店工作台</Typography.Paragraph>
        </header>
        {error && <Alert type="error" showIcon title={error} className="login-alert" />}
        <Form<LoginValues> className="login-form" layout="vertical" size="large" initialValues={{ rememberMe: false }} onFinish={submit} requiredMark={false}>
          <Form.Item name="account" label="账号" rules={[{ required: true, message: '请输入账号' }, { max: 100 }]}>
            <Input prefix={<UserOutlined />} autoComplete="username" placeholder="请输入账号" />
          </Form.Item>
          <Form.Item name="password" label="密码" rules={[{ required: true, message: '请输入密码' }, { max: 256 }]}>
            <Input.Password prefix={<LockOutlined />} autoComplete="current-password" placeholder="请输入密码" />
          </Form.Item>
          <div className="login-form-options">
            <Form.Item name="rememberMe" valuePropName="checked" noStyle><Checkbox>记住账号</Checkbox></Form.Item>
            <Button type="link" onClick={() => setRecoveryOpen(true)}>忘记密码？</Button>
          </div>
          <Button className="login-submit" type="primary" htmlType="submit" block loading={submitting}>登录</Button>
        </Form>
        <Typography.Paragraph className="login-register-copy">还没有商户账号？ <Link to="/register">立即注册</Link></Typography.Paragraph>
        <div className="login-security-note"><span aria-hidden="true">✓</span> 平台全程加密，保障经营数据安全</div>
      </div>
    </section>
    <Modal title="找回登录权限" open={recoveryOpen} onCancel={() => setRecoveryOpen(false)} footer={<Button type="primary" onClick={() => setRecoveryOpen(false)}>我知道了</Button>}>
      <Typography.Paragraph>为保护商户经营数据，员工密码需要由商户负责人或具备员工管理权限的管理员重置。</Typography.Paragraph>
      <Typography.Paragraph type="secondary">如果负责人账号也无法登录，请联系平台管理员核验商户资料后处理。</Typography.Paragraph>
    </Modal>
  </main>
}

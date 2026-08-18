import { Alert, Button, Card, Form, Input, Typography } from 'antd'
import { useNavigate } from 'react-router-dom'
import { useState } from 'react'
import { apiRequest, ApiError, resetCsrfToken } from '../api/client'

interface Values { currentPassword: string; newPassword: string; confirmation: string }
export function PlatformChangePasswordPage() {
  const navigate = useNavigate(); const [error, setError] = useState<string>(); const [loading, setLoading] = useState(false)
  const submit = async (values: Values) => {
    setLoading(true); setError(undefined)
    try { await apiRequest('/api/v1/platform/auth/change-password', { method: 'POST', body: JSON.stringify(values) }); resetCsrfToken(); navigate('/platform', { replace: true }) }
    catch (requestError) { setError(requestError instanceof ApiError ? requestError.message : '密码修改失败') }
    finally { setLoading(false) }
  }
  return <main className="platform-login-shell"><Card className="login-card" variant="borderless"><Typography.Title level={2}>首次登录修改密码</Typography.Title><Typography.Paragraph type="secondary">平台初始密码只能用于首次登录。</Typography.Paragraph>{error && <Alert type="error" showIcon title={error} className="login-alert" />}<Form<Values> layout="vertical" onFinish={submit}>
    <Form.Item name="currentPassword" label="当前初始密码" rules={[{ required: true }]}><Input.Password autoComplete="current-password" /></Form.Item>
    <Form.Item name="newPassword" label="新密码" extra="至少12位，并包含大小写字母、数字和特殊字符" rules={[{ required: true }, { min: 12 }]}><Input.Password autoComplete="new-password" /></Form.Item>
    <Form.Item name="confirmation" label="确认新密码" dependencies={['newPassword']} rules={[{ required: true }, ({ getFieldValue }) => ({ validator: (_, value) => value === getFieldValue('newPassword') ? Promise.resolve() : Promise.reject(new Error('两次密码不一致')) })]}><Input.Password autoComplete="new-password" /></Form.Item>
    <Button type="primary" htmlType="submit" block loading={loading}>保存并进入平台</Button>
  </Form></Card></main>
}

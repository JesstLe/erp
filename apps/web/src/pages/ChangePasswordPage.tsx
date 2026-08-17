import { LockOutlined, SafetyCertificateOutlined } from '@ant-design/icons'
import { Alert, Button, Card, Form, Input, Typography, message } from 'antd'
import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { apiRequest, ApiError } from '../api/client'
import type { CurrentUser } from '../api/types'
import { useAuth } from '../auth/useAuth'

interface PasswordValues { currentPassword: string; newPassword: string; confirmPassword: string }

export function ChangePasswordPage() {
  const auth = useAuth(); const navigate = useNavigate(); const [submitting, setSubmitting] = useState(false)
  const submit = async (values: PasswordValues) => {
    setSubmitting(true)
    try {
      await apiRequest<CurrentUser>('/api/v1/auth/change-password', { method: 'POST', body: JSON.stringify({ currentPassword: values.currentPassword, newPassword: values.newPassword }) })
      await auth.refresh(); message.success('密码已更新，请使用新密码继续工作'); navigate('/', { replace: true })
    } catch (error) { message.error(error instanceof ApiError ? error.message : '密码更新失败') }
    finally { setSubmitting(false) }
  }
  return <main className="password-shell"><Card className="password-card" variant="borderless"><div className="password-icon"><SafetyCertificateOutlined /></div><Typography.Title level={2}>首次登录，请先修改密码</Typography.Title><Typography.Paragraph type="secondary">初始密码只能用于首次登录。新密码至少12位，并同时包含大小写字母、数字和特殊字符。</Typography.Paragraph><Alert type="info" showIcon title="更新后当前登录会话会自动刷新，无需重复登录。" className="modal-alert" /><Form<PasswordValues> layout="vertical" size="large" onFinish={submit} requiredMark={false}><Form.Item name="currentPassword" label="当前初始密码" rules={[{ required: true }]}><Input.Password prefix={<LockOutlined />} autoComplete="current-password" /></Form.Item><Form.Item name="newPassword" label="新密码" rules={[{ required: true }, { min: 12 }, { pattern: /^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^A-Za-z0-9]).+$/, message: '需同时包含大小写字母、数字和特殊字符' }]}><Input.Password prefix={<LockOutlined />} autoComplete="new-password" /></Form.Item><Form.Item name="confirmPassword" label="再次输入新密码" dependencies={['newPassword']} rules={[{ required: true }, ({ getFieldValue }) => ({ validator: async (_, value) => { if (value !== getFieldValue('newPassword')) throw new Error('两次输入的新密码不一致') } })]}><Input.Password prefix={<LockOutlined />} autoComplete="new-password" /></Form.Item><Button type="primary" htmlType="submit" block loading={submitting}>更新密码并进入系统</Button></Form></Card></main>
}

import { CloudServerOutlined, EditOutlined, SafetyCertificateOutlined } from '@ant-design/icons'
import { Alert, Button, Card, Descriptions, Empty, Form, Input, Modal, Select, Space, Switch, Tag, Typography, message } from 'antd'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { ApiError, apiRequest } from '../api/client'
import type { PaymentChannelConfiguration } from '../api/types'
import { useAuth } from '../auth/useAuth'

interface ChannelValues { environment: string; displayName: string; credentialProfile: string; isEnabled: boolean }
const providers = [
  { code: 'WeChatPay', name: '微信支付', mode: 'Native 二维码', defaultProfile: 'PRIMARY_WECHAT' },
  { code: 'Alipay', name: '支付宝', mode: '订单码支付', defaultProfile: 'PRIMARY_ALIPAY' },
]

export function PaymentChannelsPage() {
  const auth = useAuth(); const queryClient = useQueryClient(); const [form] = Form.useForm<ChannelValues>()
  const [editing, setEditing] = useState<(typeof providers)[number]>()
  const storeId = auth.store?.id
  const configurations = useQuery({ queryKey: ['payment-channel-configurations', storeId], enabled: Boolean(storeId), queryFn: () => apiRequest<PaymentChannelConfiguration[]>(`/api/v1/payment-channels/configurations?storeId=${storeId}`) })
  const selected = configurations.data?.find((item) => item.provider === editing?.code)
  const onError = (error: unknown) => message.error(error instanceof ApiError ? error.message : '渠道配置保存失败')
  const save = useMutation({ mutationFn: (values: ChannelValues) => apiRequest<PaymentChannelConfiguration>(`/api/v1/payment-channels/configurations/${editing?.code}`, { method: 'PUT', body: JSON.stringify({ storeId, ...values, expectedVersion: selected?.version ?? 0 }) }), onSuccess: async () => { message.success('渠道配置映射已保存'); setEditing(undefined); await queryClient.invalidateQueries({ queryKey: ['payment-channel-configurations', storeId] }) }, onError })
  const open = (provider: (typeof providers)[number]) => { const item = configurations.data?.find((configuration) => configuration.provider === provider.code); form.setFieldsValue({ environment: item?.environment ?? (provider.code === 'Alipay' ? 'Sandbox' : 'Production'), displayName: item?.displayName ?? `${provider.name}${provider.mode}`, credentialProfile: item?.credentialProfile ?? provider.defaultProfile, isEnabled: item?.isEnabled ?? false }); setEditing(provider) }

  return <div className="page-stack">
    <div className="page-heading"><div><Typography.Title level={2}>支付渠道配置</Typography.Title><Typography.Paragraph>这里仅建立门店与服务器凭据配置名的映射，不录入、展示或保存商户私钥和 API 密钥。</Typography.Paragraph></div></div>
    <Alert type="warning" showIcon title="真实渠道未完成商户联调前请保持停用。系统不会把人工微信/支付宝登记自动升级为渠道成功。" />
    <div className="metric-grid">
      {providers.map((provider) => { const item = configurations.data?.find((configuration) => configuration.provider === provider.code); return <Card key={provider.code} title={<Space><CloudServerOutlined />{provider.name}<Tag>{provider.mode}</Tag></Space>} extra={<Button icon={<EditOutlined />} onClick={() => open(provider)}>配置</Button>}>
        {!item ? <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="尚未建立门店配置映射" /> : <Space orientation="vertical" size={16} className="full-width">
          <Descriptions size="small" column={1} items={[{ key: 'name', label: '显示名称', children: item.displayName }, { key: 'environment', label: '接口环境', children: <Tag color={item.environment === 'Production' ? 'purple' : 'blue'}>{item.environment === 'Production' ? '生产' : '沙箱'}</Tag> }, { key: 'profile', label: '凭据配置名', children: <Typography.Text code>{item.credentialProfile}</Typography.Text> }, { key: 'credentials', label: '服务器凭据', children: <Tag color={item.credentialsPresent ? 'green' : 'red'}>{item.credentialsPresent ? '结构完整' : '缺少配置'}</Tag> }, { key: 'enabled', label: '门店状态', children: <Tag color={item.isEnabled ? 'green' : 'default'}>{item.isEnabled ? '已启用' : '已停用'}</Tag> }]} />
          {!item.credentialsPresent && <Alert type="error" showIcon title="服务器环境仍缺少必要配置" description={item.missingRequirements.join('、')} />}
        </Space>}
      </Card> })}
    </div>
    <Alert type="info" showIcon icon={<SafetyCertificateOutlined />} title="启用时服务端会重新检查商户号、应用号、HTTPS 回调地址、密钥长度以及密钥文件是否存在；任一项不满足都拒绝启用。" />

    <Modal title={`配置${editing?.name ?? ''}`} open={Boolean(editing)} onCancel={() => setEditing(undefined)} onOk={() => form.submit()} okText="保存配置" confirmLoading={save.isPending} destroyOnHidden>
      <Alert type="info" showIcon title="凭据配置名对应服务器环境变量中的安全配置；页面不会读取私钥内容。" className="modal-alert" />
      <Form<ChannelValues> form={form} layout="vertical" onFinish={(values) => save.mutate(values)}>
        <Form.Item name="displayName" label="收银台显示名称" rules={[{ required: true }, { max: 80 }]}><Input maxLength={80} /></Form.Item>
        <Form.Item name="environment" label="接口环境" rules={[{ required: true }]}><Select options={[{ value: 'Sandbox', label: '沙箱/联调环境' }, { value: 'Production', label: '生产环境' }]} /></Form.Item>
        <Form.Item name="credentialProfile" label="服务器凭据配置名" rules={[{ required: true }, { pattern: /^[A-Z][A-Z0-9_]{2,39}$/, message: '3-40位大写字母、数字或下划线，且以字母开头' }]}><Input maxLength={40} placeholder={editing?.defaultProfile} /></Form.Item>
        <Form.Item name="isEnabled" label="门店启用" valuePropName="checked"><Switch checkedChildren="启用" unCheckedChildren="停用" /></Form.Item>
      </Form>
    </Modal>
  </div>
}

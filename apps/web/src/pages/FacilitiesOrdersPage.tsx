import { ArrowLeftOutlined } from '@ant-design/icons'
import { Button, Card, Space, Typography } from 'antd'
import { useNavigate } from 'react-router-dom'
import { CashierPage } from './CashierPage'

export function FacilitiesOrdersPage() {
  const navigate = useNavigate()

  return <div className="facilities-orders-page">
    <Card className="facilities-module-banner" variant="borderless">
      <Space className="facilities-module-banner-content" align="center">
        <Button icon={<ArrowLeftOutlined />} onClick={() => navigate('/facilities')}>返回设施看板</Button>
        <div>
          <Typography.Title level={4}>设施接待 · 消费单管理</Typography.Title>
          <Typography.Text type="secondary">补录散客消费、处理待支付账单、查询收款与核对退款。</Typography.Text>
        </div>
      </Space>
    </Card>
    <CashierPage />
  </div>
}

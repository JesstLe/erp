import { ArrowRightOutlined, ClockCircleOutlined, ExclamationCircleOutlined, SafetyCertificateOutlined, ShopOutlined } from '@ant-design/icons'
import { Button, Card, Col, Row, Space, Statistic, Tag, Typography } from 'antd'
import { useNavigate } from 'react-router-dom'
import { useAuth } from '../auth/useAuth'

export function DashboardPage() {
  const auth = useAuth(); const navigate = useNavigate()
  return <div className="page-stack">
    <section className="welcome-strip"><div><Typography.Text>今日经营工作台</Typography.Text><Typography.Title level={2}>早上好，{auth.user?.displayName}</Typography.Title><Typography.Paragraph>当前是空库开发环境。经营指标会在真实业务产生后自动汇总，不展示伪造数据。</Typography.Paragraph></div><Tag color="green" icon={<SafetyCertificateOutlined />}>权限与审计已启用</Tag></section>
    <Row gutter={[16, 16]}>
      <Col xs={24} md={12} xl={6}><Card className="metric-card"><Statistic title="今日接待" value={0} suffix="人次" prefix={<ShopOutlined />} /><span>暂无业务数据</span></Card></Col>
      <Col xs={24} md={12} xl={6}><Card className="metric-card"><Statistic title="使用中设施" value={0} suffix="个" prefix={<ClockCircleOutlined />} /><span>设施模块开发中</span></Card></Col>
      <Col xs={24} md={12} xl={6}><Card className="metric-card"><Statistic title="今日实收" value={0} precision={2} prefix="¥" /><span>不含待核对外部支付</span></Card></Col>
      <Col xs={24} md={12} xl={6}><Card className="metric-card warning"><Statistic title="待处理异常" value={0} prefix={<ExclamationCircleOutlined />} /><span>暂无异常</span></Card></Col>
    </Row>
    <Row gutter={[16, 16]}><Col xs={24} xl={15}><Card title="首个闭环" extra={<Tag color="blue">开发中</Tag>} className="flow-card"><div className="flow-steps">{['选择设施', '开始计时', '结束服务', '录入项目', '会员/支付', '完成交班'].map((label, index) => <div key={label}><span>{String(index + 1).padStart(2, '0')}</span><strong>{label}</strong></div>)}</div></Card></Col><Col xs={24} xl={9}><Card title="快速开始" className="quick-card"><Space orientation="vertical" size={12} className="full-width"><Button block size="large" onClick={() => navigate('/catalog/items')}>维护服务项目 <ArrowRightOutlined /></Button><Button block size="large" onClick={() => navigate('/catalog/prices')}>发布价格版本 <ArrowRightOutlined /></Button><Button block size="large" disabled>开始设施接待 <Tag>下一模块</Tag></Button></Space></Card></Col></Row>
  </div>
}

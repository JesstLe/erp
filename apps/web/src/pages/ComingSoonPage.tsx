import { Card, Empty, Typography } from 'antd'
export function ComingSoonPage({ title }: { title: string }) { return <div className="page-stack"><div className="page-heading"><div><Typography.Title level={2}>{title}</Typography.Title><Typography.Paragraph>该模块正在按 PRD 状态机实现，完成前不会用静态页面冒充可用功能。</Typography.Paragraph></div></div><Card variant="borderless"><Empty description="开发中" /></Card></div> }

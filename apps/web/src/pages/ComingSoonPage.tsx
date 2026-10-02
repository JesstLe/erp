import { Card, Empty, Typography } from 'antd'
export function ComingSoonPage({ title }: { title: string }) { return <div className="page-stack"><div className="page-heading"><div><Typography.Title level={2}>{title}</Typography.Title></div></div><Card variant="borderless"><Empty description="该功能暂不可用" /></Card></div> }

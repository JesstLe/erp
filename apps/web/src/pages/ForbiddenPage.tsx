import { Button, Result } from 'antd'
import { useNavigate } from 'react-router-dom'

export function ForbiddenPage() {
  const navigate = useNavigate()
  return <Result
    status="403"
    title="没有该页面的访问权限"
    subTitle="当前岗位未被授予此页面权限。如工作职责发生变化，请联系最高权限账号调整员工角色。"
    extra={<Button type="primary" onClick={() => navigate('/', { replace: true })}>返回经营工作台</Button>}
  />
}

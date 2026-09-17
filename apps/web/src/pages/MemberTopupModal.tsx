import { DeleteOutlined, PrinterOutlined, PlusOutlined } from '@ant-design/icons'
import { Alert, Button, Card, Form, Input, InputNumber, Modal, Select, Space, message } from 'antd'
import { useMutation } from '@tanstack/react-query'
import { useState } from 'react'
import { apiRequest, ApiError } from '../api/client'
import type { MemberCard, MemberTopup, PaymentMethod } from '../api/types'

interface TopupAllocationValues {
  methodId: string
  amountYuan: number
  externalReference?: string
}

interface TopupValues {
  cardId: string
  principalYuan: number
  bonusYuan: number
  note?: string
  allocations: TopupAllocationValues[]
}

interface Props {
  open: boolean
  storeId: string
  storeName?: string
  customerId: string
  customerName: string
  cards: MemberCard[]
  methods: PaymentMethod[]
  shiftOpen: boolean
  shiftLoading?: boolean
  canGrantBonus: boolean
  onClose: () => void
  onSuccess: (topup: MemberTopup) => Promise<void> | void
}

function requestError(error: unknown) {
  return error instanceof ApiError ? error.message : error instanceof Error ? error.message : '储值失败，请稍后重试'
}

function money(minor: number) {
  return `¥${(minor / 100).toFixed(2)}`
}

export function MemberTopupModal({ open, storeId, storeName, customerId, customerName, cards, methods,
  shiftOpen, shiftLoading, canGrantBonus, onClose, onSuccess }: Props) {
  const [form] = Form.useForm<TopupValues>()
  const [completedTopup, setCompletedTopup] = useState<MemberTopup>()
  const allowedMethods = methods.filter((method) =>
    method.category !== 'InternalAccount' && method.category !== 'ChannelExternal')
  const createTopup = useMutation({
    mutationFn: (values: TopupValues) => apiRequest<MemberTopup>('/api/v1/member-topups', {
      method: 'POST',
      body: JSON.stringify({
        storeId,
        customerId,
        cardId: values.cardId,
        principalMinor: Math.round(values.principalYuan * 100),
        bonusMinor: Math.round(values.bonusYuan * 100),
        note: values.note?.trim() || null,
        commandId: crypto.randomUUID(),
        allocations: values.allocations.map((line) => ({
          methodId: line.methodId,
          amountMinor: Math.round(line.amountYuan * 100),
          externalReference: line.externalReference?.trim() || null,
        })),
      }),
    }),
    onSuccess: async (topup) => {
      message.success(`储值成功：本金 ${money(topup.principalMinor)}，赠金 ${money(topup.bonusMinor)}`)
      setCompletedTopup(topup)
      await onSuccess(topup)
    },
    onError: (error) => message.error(requestError(error)),
  })
  const initialize = (visible: boolean) => {
    if (!visible) return
    setCompletedTopup(undefined)
    const firstMethod = allowedMethods.find((method) => method.code === 'CASH') ?? allowedMethods[0]
    form.setFieldsValue({
      cardId: cards[0]?.id,
      principalYuan: 0,
      bonusYuan: 0,
      allocations: [{ methodId: firstMethod?.id, amountYuan: 0 }],
    })
  }
  const close = () => {
    setCompletedTopup(undefined)
    form.resetFields()
    onClose()
  }
  const receiptCard = cards.find((card) => card.id === completedTopup?.cardId)

  return <Modal
    title={completedTopup ? `储值完成 · ${completedTopup.topupNo}` : `会员储值 · ${customerName}`}
    width={760}
    open={open}
    onCancel={close}
    onOk={completedTopup ? undefined : () => form.submit()}
    afterOpenChange={initialize}
    confirmLoading={createTopup.isPending}
    okText="确认收款并入账"
    okButtonProps={{ disabled: shiftLoading || !shiftOpen || !cards.length || !allowedMethods.length }}
    footer={completedTopup ? <Space><Button onClick={close}>完成</Button><Button type="primary"
      icon={<PrinterOutlined />} aria-label="打印储值小票" onClick={() => window.print()}>打印储值小票</Button></Space> : undefined}
    destroyOnHidden
  >
    {completedTopup ? <div className="modern-prebill modern-pos-receipt">
      <h2>{storeName || '当前门店'}</h2>
      <p>储值单：{completedTopup.topupNo}</p>
      <p>支付单：{completedTopup.paymentNo}</p>
      <p>时间：{new Date(completedTopup.paidAtUtc).toLocaleString('zh-CN', { hour12: false })}</p>
      <p>会员：{customerName}</p>
      <p>会员卡：{receiptCard ? `${receiptCard.maskedCardNo} · ${receiptCard.cardTypeName}` : completedTopup.cardId}</p>
      <div><span>储值本金</span><b>{money(completedTopup.principalMinor)}</b></div>
      <div><span>赠送金额</span><b>{money(completedTopup.bonusMinor)}</b></div>
      {completedTopup.allocations.map((allocation) => <div key={allocation.id}>
        <span>{allocation.methodName}{allocation.reconciliationStatus === 'Pending' ? '（待核对）' : ''}</span>
        <b>{money(allocation.amountMinor)}</b>
      </div>)}
      {completedTopup.note && <p>备注：{completedTopup.note}</p>}
      <footer>本次入账 <b>{money(completedTopup.principalMinor + completedTopup.bonusMinor)}</b></footer>
    </div> : <><Alert type="warning" showIcon className="modal-alert"
      title="本金是本次实际收款；赠金不计入实收。确认后立即写入会员资金流水，不能直接修改余额。" />
    {!shiftLoading && !shiftOpen && <Alert type="error" showIcon className="modal-alert" title="请先开班，再办理会员储值。" />}
    {!allowedMethods.length && <Alert type="error" showIcon className="modal-alert" title="当前没有可用于即时储值的收款方式。" />}
    <Form<TopupValues> form={form} layout="vertical" onFinish={(values) => createTopup.mutate(values)}>
      <Form.Item name="cardId" label="存入会员卡" rules={[{ required: true, message: '请选择会员卡' }]}>
        <Select options={cards.map((card) => {
          const principal = card.accounts.filter((account) => account.accountType === 'Principal' && account.status.toUpperCase() === 'ACTIVE').reduce((sum, account) => sum + account.balanceUnits, 0)
          const bonus = card.accounts.filter((account) => account.accountType === 'Bonus' && account.status.toUpperCase() === 'ACTIVE').reduce((sum, account) => sum + account.balanceUnits, 0)
          return { value: card.id, label: `${card.maskedCardNo} · ${card.cardTypeName} · 本金 ${money(principal)} · 赠金 ${money(bonus)}` }
        })} />
      </Form.Item>
      <div className="two-column-form">
        <Form.Item name="principalYuan" label="储值本金（元）" rules={[{ required: true }, { type: 'number', min: 0.01, max: 100000000 }]}>
          <InputNumber min={0.01} max={100000000} precision={2} prefix="¥" className="full-width" />
        </Form.Item>
        <Form.Item name="bonusYuan" label={canGrantBonus ? '赠送金额（元）' : '赠送金额（仅最高权限可填）'}
          rules={[{ required: true }, { type: 'number', min: 0, max: 100000000 }]}>
          <InputNumber min={0} max={100000000} precision={2} prefix="¥" className="full-width" disabled={!canGrantBonus} />
        </Form.Item>
      </div>
      <Form.List name="allocations" rules={[{
        validator: async (_, lines: TopupAllocationValues[]) => {
          const principal = Math.round(Number(form.getFieldValue('principalYuan') ?? 0) * 100)
          const total = (lines ?? []).reduce((sum, line) => sum + Math.round(Number(line.amountYuan ?? 0) * 100), 0)
          if (total !== principal) throw new Error(`支付分摊必须等于储值本金 ${money(principal)}`)
          const methodIds = (lines ?? []).map((line) => line.methodId).filter(Boolean)
          if (new Set(methodIds).size !== methodIds.length) throw new Error('同一支付方式不能重复添加')
        },
      }]}>
        {(fields, { add, remove }, { errors }) => <>
          <div className="order-line-list">{fields.map((field) => <TopupPaymentEditor
            key={field.key} field={field} form={form} methods={allowedMethods}
            removable={fields.length > 1} onRemove={() => remove(field.name)} />)}</div>
          <Space><Button icon={<PlusOutlined />} onClick={() => add({ amountYuan: 0 })}>添加支付方式</Button><Form.ErrorList errors={errors} /></Space>
        </>}
      </Form.List>
      <Form.Item name="note" label="储值备注（可选）" rules={[{ max: 500 }]}>
        <Input.TextArea rows={2} maxLength={500} showCount />
      </Form.Item>
    </Form></>}
  </Modal>
}

function TopupPaymentEditor({ field, form, methods, removable, onRemove }: {
  field: { key: number; name: number }
  form: ReturnType<typeof Form.useForm<TopupValues>>[0]
  methods: PaymentMethod[]
  removable: boolean
  onRemove: () => void
}) {
  const methodId = Form.useWatch(['allocations', field.name, 'methodId'], form)
  const method = methods.find((item) => item.id === methodId)
  return <Card size="small" className="order-line-editor" extra={removable &&
    <Button type="text" danger icon={<DeleteOutlined />} onClick={onRemove} aria-label="删除支付分摊" />}>
    <div className="payment-line-fields">
      <Form.Item name={[field.name, 'methodId']} label="支付方式" rules={[{ required: true }]}>
        <Select options={methods.map((item) => ({ value: item.id, label: item.name }))} />
      </Form.Item>
      <Form.Item name={[field.name, 'amountYuan']} label="实收金额（元）"
        rules={[{ required: true }, { type: 'number', min: 0.01, max: 100000000 }]}>
        <InputNumber min={0.01} max={100000000} precision={2} prefix="¥" aria-label="实收金额（元）" />
      </Form.Item>
    </div>
    {method?.category === 'ManualExternal' && method.code !== 'WECHAT_MANUAL' && <>
      <Form.Item name={[field.name, 'externalReference']} label="交易参考号"
        rules={[{ required: true, message: '人工外部收款必须填写参考号' }, { min: 4 }, { max: 100 }]}>
        <Input maxLength={100} aria-label="交易参考号" />
      </Form.Item>
    </>}
    {method?.category === 'ManualExternal' &&
      <Alert type="warning" showIcon title="人工登记只进入待对账，不能代表渠道确认到账。" />}
  </Card>
}

import { useState, type Key } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Alert, Button, Input, InputNumber, Modal, Space, Table, Typography, message } from "antd";
import { apiRequest, ApiError } from "../api/client";
import type { EmployeePosition, PositionCommissions, PositionServiceCommission } from "../api/types";

import { commissionLabel } from "./positionCommission";

// Shared configuration UI, without routing either shell into the other shell.
export function PositionCommissionsModal({ position, onClose }: {
  position: EmployeePosition; onClose: () => void;
}) {
  const queryClient = useQueryClient();
  const path = `/api/v1/employees/positions/${position.id}/commissions`;
  const configuration = useQuery({
    queryKey: ["position-commissions", position.id],
    queryFn: ({ signal }) => apiRequest<PositionCommissions>(path, { signal }),
    refetchOnMount: "always", refetchOnWindowFocus: false, refetchOnReconnect: false,
  });
  const [editedDefaultRate, setDefaultRate] = useState<number | null | undefined>();
  const [editedRates, setRates] = useState<Record<string, number | undefined> | null>(null);
  const defaultRate = editedDefaultRate === undefined
    ? configuration.data?.position.defaultCommissionRateBasisPoints ?? null : editedDefaultRate;
  const rates = editedRates ?? Object.fromEntries((configuration.data?.services ?? [])
    .filter(x => x.rateBasisPoints != null).map(x => [x.serviceItemId, x.rateBasisPoints!])) as Record<string, number | undefined>;
  const [search, setSearch] = useState("");
  const [selected, setSelected] = useState<Key[]>([]);
  const [batchPercent, setBatchPercent] = useState<number | null>(null);
  const save = useMutation({
    mutationFn: () => apiRequest<PositionCommissions>(path, {
      method: "PUT",
      body: JSON.stringify({ defaultRateBasisPoints: defaultRate,
        expectedVersion: configuration.data!.position.version,
        services: Object.entries(rates).filter(([, rate]) => rate != null)
          .map(([serviceItemId, rateBasisPoints]) => ({ serviceItemId, rateBasisPoints })),
      }),
    }),
    onSuccess: async () => {
      message.success("岗位提成已保存；已确认的历史消费单不受影响");
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ["employee-positions"] }),
        queryClient.invalidateQueries({ queryKey: ["position-commissions", position.id] }),
      ]);
      onClose();
    },
    onError: (error: unknown) => message.error(error instanceof ApiError ? error.message : "保存失败"),
  });
  const busy = configuration.isFetching || save.isPending;
  const services = (configuration.data?.services ?? []).filter(x =>
    !search.trim() || `${x.code} ${x.name}`.toLowerCase().includes(search.trim().toLowerCase()));
  const applyBatch = (value: number | undefined) => setRates(previous => ({ ...(previous ?? rates),
    ...Object.fromEntries(selected.map(id => [String(id), value])),
  }));
  return <Modal open title={`岗位提成设置 · ${position.name}`} width={960}
    onCancel={onClose} onOk={() => save.mutate()} okText="保存提成规则"
    confirmLoading={save.isPending} okButtonProps={{ disabled: !configuration.data || busy || configuration.isError }}>
    <Space orientation="vertical" style={{ width: "100%" }} size={16}>
      <Alert type="info" showIcon title="员工选择该岗位后自动使用这些比例。项目单独比例优先，其次岗位默认，最后沿用项目原有提成。留空表示沿用，0% 表示不计提；只影响新建或重新保存的草稿。" />
      {configuration.isError && <Alert type="error" title="提成配置加载失败，请重试" />}
      <Space wrap>
        <Typography.Text>岗位默认提成比例</Typography.Text>
        <InputNumber aria-label="岗位默认提成比例" min={0} max={100} precision={2} suffix="%"
          disabled={busy || !configuration.data} value={defaultRate == null ? null : defaultRate / 100}
          onChange={value => setDefaultRate(value == null ? null : Math.round(value * 100))}
          placeholder="留空沿用项目规则" style={{ width: 210 }} />
        <Button disabled={busy || !configuration.data} onClick={() => setDefaultRate(null)}>清空默认比例</Button>
        <Button disabled={busy} onClick={() => { setDefaultRate(undefined); setRates(null); void configuration.refetch(); }}>重新加载</Button>
      </Space>
      <Space wrap>
        <Input aria-label="查询提成服务项目" placeholder="输入服务项目名称或编号" allowClear value={search}
          onChange={event => setSearch(event.target.value)} style={{ width: 240 }} />
        <Button disabled={busy || !services.length} onClick={() => setSelected(services.map(x => x.serviceItemId))}>全选查询结果</Button>
        <InputNumber aria-label="批量提成比例" min={0} max={100} precision={2} suffix="%"
          value={batchPercent} onChange={setBatchPercent} placeholder="批量比例" style={{ width: 135 }} />
        <Button disabled={busy || !selected.length || batchPercent == null}
          onClick={() => applyBatch(Math.round(batchPercent! * 100))}>应用到选中项目</Button>
        <Button disabled={busy || !selected.length} onClick={() => applyBatch(undefined)}>清空选中项目比例</Button>
      </Space>
      <Table<PositionServiceCommission> size="small" rowKey="serviceItemId" loading={configuration.isFetching}
        dataSource={services} pagination={{ pageSize: 10, showSizeChanger: false }} scroll={{ x: 750, y: 340 }}
        rowSelection={{ selectedRowKeys: selected, onChange: setSelected, preserveSelectedRowKeys: true,
          getCheckboxProps: () => ({ disabled: busy }) }}
        columns={[
          { title: "项目编号", dataIndex: "code", width: 150 },
          { title: "服务项目", dataIndex: "name", width: 200,
            render: (_, item) => `${item.name}${item.status === "Disabled" ? "（已停用）" : ""}` },
          { title: "该岗位的项目提成比例", width: 220, render: (_, item) =>
            <InputNumber aria-label={`${item.name}提成比例`} min={0} max={100} precision={2} suffix="%"
              value={rates[item.serviceItemId] == null ? null : rates[item.serviceItemId]! / 100}
              disabled={busy} placeholder="留空沿用岗位默认" style={{ width: "100%" }}
              onChange={value => setRates(previous => ({ ...(previous ?? rates),
                [item.serviceItemId]: value == null ? undefined : Math.round(value * 100),
              }))} /> },
          { title: "实际生效规则", width: 200, render: (_, item) =>
            commissionLabel(item, rates[item.serviceItemId], defaultRate) },
        ]} />
      <Typography.Text type="secondary">比例按服务明细最终成交金额计算（包含数量），金额四舍五入到分；产品不计服务提成。合伙人暂未纳入。</Typography.Text>
    </Space>
  </Modal>;
}

import { useState, type Key } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Alert, Button, Input, InputNumber, Modal, Space, Table, Typography, message } from "antd";
import { apiRequest, ApiError } from "../api/client";
import type { EmployeePosition, PositionCommissions, PositionServiceCommission } from "../api/types";

import { commissionLabel, commissionSourceLabel } from "./positionCommission";

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
      <Alert type="info" showIcon title="提成只算一次，不叠加" description={<div>
        <div>先填岗位通用比例；个别项目不同，再在下面单独填。</div>
        <div>项目单独填了，就用单独比例；没填，就用岗位通用比例。</div>
        <div>两处都没填，就用“服务项目”页面设置的提成。</div>
        <div>例如：岗位填20%，某项目单独填15%；该项目实收100元，提成是15元，不是35元。</div>
      </div>} />
      {configuration.isError && <Alert type="error" title="提成配置加载失败，请重试" />}
      <Space wrap>
        <Typography.Text>岗位通用比例</Typography.Text>
        <InputNumber aria-label="岗位通用比例" min={0} max={100} precision={2} suffix="%"
          disabled={busy || !configuration.data} value={defaultRate == null ? null : defaultRate / 100}
          onChange={value => setDefaultRate(value == null ? null : Math.round(value * 100))}
          placeholder="不填则用项目原设置" style={{ width: 210 }} />
        <Button disabled={busy || !configuration.data} onClick={() => setDefaultRate(null)}>清空通用比例</Button>
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
          { title: "单独比例（可不填）", width: 220, render: (_, item) =>
            <InputNumber aria-label={`${item.name}提成比例`} min={0} max={100} precision={2} suffix="%"
              value={rates[item.serviceItemId] == null ? null : rates[item.serviceItemId]! / 100}
              disabled={busy} placeholder="不填则用通用比例" style={{ width: "100%" }}
              onChange={value => setRates(previous => ({ ...(previous ?? rates),
                [item.serviceItemId]: value == null ? undefined : Math.round(value * 100),
              }))} /> },
          { title: "最终按这个算", width: 200, render: (_, item) => <div>
            <Typography.Text strong>{commissionLabel(item, rates[item.serviceItemId], defaultRate)}</Typography.Text>
            <div><Typography.Text type="secondary">{commissionSourceLabel(rates[item.serviceItemId], defaultRate)}</Typography.Text></div>
          </div> },
        ]} />
      <Typography.Text type="secondary">按服务成交金额（含数量）计算提成，四舍五入到分；产品不计服务提成。</Typography.Text>
      <Typography.Text type="secondary">不填不等于0%：填0%就是不给提成。已确认的账单不会因修改比例而改变。</Typography.Text>
    </Space>
  </Modal>;
}

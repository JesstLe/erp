import { Modal, Space, Tag, Typography } from "antd";
import type { Employee } from "../api/types";
import { EmployeeCommissionsPage } from "../pages/EmployeeCommissionsPage";
import { commissionDateRange } from "./employeeCommissionDates";

export function EmployeeServiceHistoryModal({ employee, positionName, classic = false, onClose }: {
  employee: Employee; positionName: string; classic?: boolean; onClose: () => void;
}) {
  return <Modal open title={`${employee.displayName} · 服务与提成`} width="min(1320px, 96vw)" onCancel={onClose}
    footer={null} destroyOnHidden styles={{ body: { maxHeight: "72vh", overflowY: "auto" } }}>
    <Space wrap style={{ marginBottom: 16 }}>
      <Tag>{employee.employeeNo}</Tag><Tag>{positionName}</Tag>
      <Tag color={employee.status === "Active" ? "green" : "default"}>{employee.status === "Active" ? "在职" : "已离职"}</Tag>
      <Typography.Text type="secondary">当前所属门店：{employee.stores.map(x => x.name).join("、") || "未设置"}；历史记录仍按当时实际门店查询。</Typography.Text>
    </Space>
    <EmployeeCommissionsPage classic={classic} employee={employee} initialDates={commissionDateRange(1)} />
  </Modal>;
}

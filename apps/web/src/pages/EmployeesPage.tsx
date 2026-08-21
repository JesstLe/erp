import {
  EditOutlined,
  KeyOutlined,
  LoadingOutlined,
  PlusOutlined,
  SafetyCertificateOutlined,
  SearchOutlined,
  StopOutlined,
  UserOutlined,
} from "@ant-design/icons";
import {
  Alert,
  Button,
  Card,
  Checkbox,
  Descriptions,
  Drawer,
  Empty,
  Form,
  Input,
  Modal,
  Popconfirm,
  Select,
  Space,
  Table,
  Tag,
  Typography,
  message,
} from "antd";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useEffect, useState } from "react";
import { apiRequest, ApiError } from "../api/client";
import type { Employee, EmployeeRole, PageResult } from "../api/types";
import { useAuth } from "../auth/useAuth";
import { useDebouncedValue } from "../hooks/useDebouncedValue";
import { PASSWORD_POLICY_HINT, passwordRules } from "../security/passwordPolicy";

interface EmployeeValues {
  displayName: string;
  positionCode: string;
  storeIds: string[];
  createLoginAccount: boolean;
  account?: string;
  initialPassword?: string;
  roles?: string[];
}
interface EditValues {
  displayName: string;
  positionCode: string;
  storeIds: string[];
  roles: string[];
}
interface EmploymentValues {
  reason: string;
}
interface ResetPasswordValues {
  newInitialPassword: string;
  reason: string;
}

const positionOptions = [
  { value: "OWNER", label: "负责人" },
  { value: "STORE_MANAGER", label: "店长" },
  { value: "FRONT_DESK", label: "前台" },
  { value: "CASHIER", label: "收银员" },
  { value: "TECHNICIAN", label: "服务员工" },
  { value: "OTHER", label: "其他岗位" },
];
const roleColor: Record<string, string> = {
  OWNER: "purple",
  STORE_MANAGER: "blue",
  FRONT_DESK: "cyan",
  CASHIER: "green",
  TECHNICIAN: "default",
};

export function EmployeesPage() {
  const auth = useAuth();
  const queryClient = useQueryClient();
  const [createForm] = Form.useForm<EmployeeValues>();
  const [editForm] = Form.useForm<EditValues>();
  const [employmentForm] = Form.useForm<EmploymentValues>();
  const [passwordForm] = Form.useForm<ResetPasswordValues>();
  const [createOpen, setCreateOpen] = useState(false);
  const [editOpen, setEditOpen] = useState(false);
  const [employmentOpen, setEmploymentOpen] = useState(false);
  const [passwordOpen, setPasswordOpen] = useState(false);
  const [selected, setSelected] = useState<Employee>();
  const [queryText, setQueryText] = useState("");
  const appliedQuery = useDebouncedValue(queryText.trim());
  const [page, setPage] = useState(1);
  const pageSize = 20;
  useEffect(() => setPage(1), [appliedQuery]);
  const employeeParams = new URLSearchParams({
    page: String(page),
    pageSize: String(pageSize),
  });
  if (appliedQuery) employeeParams.set("query", appliedQuery);
  const employeePath = `/api/v1/employees${employeeParams.size ? `?${employeeParams}` : ""}`;
  const employees = useQuery({
    queryKey: ["employees", appliedQuery, page],
    queryFn: ({ signal }) =>
      apiRequest<PageResult<Employee>>(employeePath, { signal }),
  });
  const roles = useQuery({
    queryKey: ["employee-roles"],
    queryFn: () => apiRequest<EmployeeRole[]>("/api/v1/employees/roles"),
  });
  const loginEnabled = Form.useWatch("createLoginAccount", createForm);
  const onError = (error: unknown) =>
    message.error(error instanceof ApiError ? error.message : "操作失败");
  const refreshEmployee = async (employee: Employee, success: string) => {
    message.success(success);
    setSelected(employee);
    await queryClient.invalidateQueries({ queryKey: ["employees"] });
  };
  const roleLabel = (code: string) =>
    roles.data?.find((role) => role.code === code)?.name ?? code;
  const create = useMutation({
    mutationFn: (values: EmployeeValues) =>
      apiRequest<Employee>("/api/v1/employees", {
        method: "POST",
        body: JSON.stringify({
          ...values,
          account: values.createLoginAccount ? values.account : null,
          initialPassword: values.createLoginAccount
            ? values.initialPassword
            : null,
          roles: values.createLoginAccount ? values.roles : [],
        }),
      }),
    onSuccess: async (employee) => {
      setCreateOpen(false);
      createForm.resetFields();
      await refreshEmployee(employee, `员工已创建，系统工号 ${employee.employeeNo}；初始密码不会回显`);
    },
    onError,
  });
  const update = useMutation({
    mutationFn: (values: EditValues) =>
      apiRequest<Employee>(`/api/v1/employees/${selected!.id}`, {
        method: "PUT",
        body: JSON.stringify({ ...values, expectedVersion: selected!.version }),
      }),
    onSuccess: async (employee) => {
      setEditOpen(false);
      await refreshEmployee(employee, "员工资料已更新");
    },
    onError,
  });
  const setAccountStatus = useMutation({
    mutationFn: ({
      employee,
      isEnabled,
    }: {
      employee: Employee;
      isEnabled: boolean;
    }) =>
      apiRequest<Employee>(`/api/v1/employees/${employee.id}/account-status`, {
        method: "POST",
        body: JSON.stringify({ isEnabled }),
      }),
    onSuccess: async (employee) =>
      refreshEmployee(
        employee,
        employee.accountEnabled ? "登录账号已启用" : "登录账号已停用",
      ),
    onError,
  });
  const changeEmployment = useMutation({
    mutationFn: (values: EmploymentValues) =>
      apiRequest<Employee>(
        `/api/v1/employees/${selected!.id}/employment-status`,
        {
          method: "POST",
          body: JSON.stringify({
            reactivate: selected!.status !== "Active",
            reason: values.reason,
            expectedVersion: selected!.version,
          }),
        },
      ),
    onSuccess: async (employee) => {
      setEmploymentOpen(false);
      employmentForm.resetFields();
      await refreshEmployee(
        employee,
        employee.status === "Active"
          ? "员工已恢复在职；登录账号仍需单独启用"
          : "员工已离职，登录账号已同步停用",
      );
    },
    onError,
  });
  const resetPassword = useMutation({
    mutationFn: (values: ResetPasswordValues) =>
      apiRequest<Employee>(`/api/v1/employees/${selected!.id}/reset-password`, {
        method: "POST",
        body: JSON.stringify(values),
      }),
    onSuccess: async (employee) => {
      setPasswordOpen(false);
      passwordForm.resetFields();
      await refreshEmployee(
        employee,
        "初始密码已重置；员工下次登录必须修改密码",
      );
    },
    onError,
  });

  const openCreate = () => {
    createForm.setFieldsValue({
      createLoginAccount: true,
      storeIds: auth.store ? [auth.store.id] : [],
      positionCode: "STORE_MANAGER",
      roles: ["STORE_MANAGER"],
    });
    setCreateOpen(true);
  };
  const openEdit = () => {
    if (!selected) return;
    editForm.setFieldsValue({
      displayName: selected.displayName,
      positionCode: selected.positionCode,
      storeIds: selected.stores.map((store) => store.id),
      roles: selected.roles,
    });
    setEditOpen(true);
  };
  const columns = [
    {
      title: "员工",
      key: "employee",
      render: (_: unknown, record: Employee) => (
        <div className="employee-cell">
          <span>
            <UserOutlined />
          </span>
          <div>
            <strong>{record.displayName}</strong>
            <Typography.Text type="secondary">
              {record.employeeNo}
            </Typography.Text>
          </div>
        </div>
      ),
    },
    {
      title: "岗位",
      dataIndex: "positionCode",
      render: (value: string) =>
        positionOptions.find((item) => item.value === value)?.label ?? value,
    },
    {
      title: "在职状态",
      dataIndex: "status",
      render: (value: string) => (
        <Tag color={value === "Active" ? "green" : "default"}>
          {value === "Active" ? "在职" : "已离职"}
        </Tag>
      ),
    },
    {
      title: "所属门店",
      dataIndex: "stores",
      render: (stores: Employee["stores"]) =>
        stores.map((store) => (
          <Tag key={store.id} color={store.isPrimary ? "blue" : undefined}>
            {store.name}
          </Tag>
        )),
    },
    {
      title: "登录账号",
      key: "account",
      render: (_: unknown, record: Employee) =>
        record.account ? (
          <div className="account-state">
            <strong>{record.account}</strong>
            <Tag color={record.accountEnabled ? "green" : "default"}>
              {record.accountEnabled ? "可登录" : "已停用"}
            </Tag>
          </div>
        ) : (
          <Typography.Text type="secondary">未开通</Typography.Text>
        ),
    },
    {
      title: "角色",
      dataIndex: "roles",
      render: (values: string[]) =>
        values.length ? (
          values.map((role) => (
            <Tag key={role} color={roleColor[role]}>
              {roleLabel(role)}
            </Tag>
          ))
        ) : (
          <Typography.Text type="secondary">无登录角色</Typography.Text>
        ),
    },
    {
      title: "操作",
      key: "action",
      width: 90,
      render: (_: unknown, record: Employee) => (
        <Button
          size="small"
          onClick={(event) => {
            event.stopPropagation();
            setSelected(record);
          }}
        >
          查看
        </Button>
      ),
    },
  ];
  const isSelf = selected?.userId === auth.user?.id;
  const storeOptions = auth.user?.stores.map((store) => ({
    value: store.id,
    label: `${store.name} · ${store.code}`,
  }));
  const roleOptions = roles.data?.map((role) => ({
    value: role.code,
    label: `${role.name} · ${role.code}`,
  }));

  return (
    <div className="page-stack">
      <div className="page-heading">
        <div>
          <Typography.Title level={2}>员工与登录账号</Typography.Title>
          <Typography.Paragraph>
            员工档案、在职状态、登录凭据、角色和门店范围分别管理；离职与停用均保留历史业务。
          </Typography.Paragraph>
        </div>
        <Button type="primary" icon={<PlusOutlined />} onClick={openCreate}>
          新增员工
        </Button>
      </div>
      <Alert
        type="info"
        showIcon
        title="只有最高权限账号可以维护员工权限。密码不会回显或写入审计日志；新密码首次使用后必须修改。"
      />
      <Card variant="borderless">
        <Space wrap>
          <Input
            value={queryText}
            onChange={(event) => setQueryText(event.target.value)}
            allowClear
            maxLength={100}
            placeholder="输入姓名、工号、账号、岗位或门店，自动匹配"
            prefix={<SearchOutlined />}
            suffix={
              queryText.trim() !== appliedQuery || employees.isFetching ? (
                <LoadingOutlined spin />
              ) : null
            }
            aria-label="实时查询员工"
            style={{ width: 420 }}
          />
          <Button onClick={() => setQueryText("")}>重置</Button>
          <Typography.Text type="secondary">
            输入后自动加载，无需点击查询
          </Typography.Text>
        </Space>
      </Card>
      {employees.error && (
        <Alert
          type="error"
          showIcon
          title={
            employees.error instanceof Error
              ? employees.error.message
              : "员工查询失败"
          }
        />
      )}
      <Card variant="borderless" className="table-card">
        <Table<Employee>
          rowKey="id"
          columns={columns}
          dataSource={employees.data?.items}
          loading={employees.isFetching}
          pagination={{
            current: page,
            pageSize,
            total: employees.data?.total ?? 0,
            showSizeChanger: false,
            showTotal: (total) => `共 ${total} 位员工`,
            onChange: setPage,
          }}
          locale={{ emptyText: <Empty description="没有匹配的员工档案" /> }}
          onRow={(record) => ({
            onClick: () => setSelected(record),
            className: "clickable-row",
          })}
        />
      </Card>

      <Modal
        title="新增员工"
        width={720}
        open={createOpen}
        onCancel={() => setCreateOpen(false)}
        onOk={() => createForm.submit()}
        okText="创建员工"
        confirmLoading={create.isPending}
        destroyOnHidden
      >
        <Alert
          type="warning"
          showIcon
          title="初始密码仅用于本次提交，保存后不会再次显示。请通过安全方式单独告知员工。"
          className="modal-alert"
        />
        <Form<EmployeeValues>
          form={createForm}
          layout="vertical"
          onFinish={(values) => create.mutate(values)}
          requiredMark="optional"
        >
          <Alert
            type="info"
            showIcon
            title="员工保存后由系统自动生成品牌内唯一工号，例如 EMP000001。"
            className="modal-alert"
          />
          <div className="employee-form-grid">
            <Form.Item
              name="displayName"
              label="员工姓名"
              rules={[{ required: true }, { min: 2 }, { max: 100 }]}
            >
              <Input maxLength={100} />
            </Form.Item>
          </div>
          <div className="employee-form-grid">
            <Form.Item
              name="positionCode"
              label="岗位"
              rules={[{ required: true }]}
            >
              <Select options={positionOptions} />
            </Form.Item>
            <Form.Item
              name="storeIds"
              label="所属门店"
              rules={[
                {
                  required: true,
                  type: "array",
                  min: 1,
                  message: "至少选择一个门店",
                },
              ]}
            >
              <Select mode="multiple" options={storeOptions} />
            </Form.Item>
          </div>
          <Form.Item name="createLoginAccount" valuePropName="checked">
            <Checkbox>同时开通登录账号</Checkbox>
          </Form.Item>
          {loginEnabled && (
            <Card size="small" className="login-account-fields">
              <div className="employee-form-grid">
                <Form.Item
                  name="account"
                  label="登录账号"
                  rules={[
                    { required: true },
                    {
                      pattern: /^[A-Za-z0-9._@-]{4,100}$/,
                      message: "仅限4-100位字母、数字及 . _ @ -",
                    },
                  ]}
                >
                  <Input maxLength={100} autoComplete="off" />
                </Form.Item>
                <Form.Item
                  name="initialPassword"
                  label="初始密码"
                  extra={PASSWORD_POLICY_HINT}
                  rules={passwordRules("请输入初始密码")}
                >
                  <Input.Password maxLength={256} autoComplete="new-password" />
                </Form.Item>
              </div>
              <Form.Item
                name="roles"
                label="登录角色"
                rules={[
                  {
                    required: true,
                    type: "array",
                    min: 1,
                    message: "至少选择一个角色",
                  },
                ]}
              >
                <Select mode="multiple" options={roleOptions} />
              </Form.Item>
            </Card>
          )}
        </Form>
      </Modal>

      <Modal
        title="编辑员工资料与权限"
        width={680}
        open={editOpen}
        onCancel={() => setEditOpen(false)}
        onOk={() => editForm.submit()}
        okText="保存修改"
        confirmLoading={update.isPending}
        destroyOnHidden
      >
        <Form<EditValues>
          form={editForm}
          layout="vertical"
          onFinish={(values) => update.mutate(values)}
        >
          <div className="employee-form-grid">
            <Form.Item
              name="displayName"
              label="员工姓名"
              rules={[{ required: true }, { min: 2 }, { max: 100 }]}
            >
              <Input maxLength={100} />
            </Form.Item>
            <Form.Item
              name="positionCode"
              label="岗位"
              rules={[{ required: true }]}
            >
              <Select options={positionOptions} />
            </Form.Item>
          </div>
          <Form.Item
            name="storeIds"
            label="所属门店"
            rules={[
              {
                required: true,
                type: "array",
                min: 1,
                message: "至少选择一个门店",
              },
            ]}
          >
            <Select mode="multiple" options={storeOptions} />
          </Form.Item>
          {selected?.account ? (
            <Form.Item
              name="roles"
              label="登录角色"
              rules={[
                {
                  required: true,
                  type: "array",
                  min: 1,
                  message: "至少保留一个角色",
                },
              ]}
            >
              <Select mode="multiple" options={roleOptions} />
            </Form.Item>
          ) : (
            <Alert
              type="info"
              showIcon
              title="该员工没有登录账号，因此不分配系统角色。"
            />
          )}
        </Form>
      </Modal>

      <Modal
        title={selected?.status === "Active" ? "办理员工离职" : "恢复员工在职"}
        open={employmentOpen}
        onCancel={() => setEmploymentOpen(false)}
        onOk={() => employmentForm.submit()}
        okText={selected?.status === "Active" ? "确认离职" : "确认复职"}
        okButtonProps={{ danger: selected?.status === "Active" }}
        confirmLoading={changeEmployment.isPending}
        destroyOnHidden
      >
        <Alert
          type={selected?.status === "Active" ? "warning" : "info"}
          showIcon
          title={
            selected?.status === "Active"
              ? "离职后登录账号会立即停用，历史订单、提成和审计记录不会删除。"
              : "复职不会自动恢复登录权限，需要另行启用账号。"
          }
          className="modal-alert"
        />
        <Form<EmploymentValues>
          form={employmentForm}
          layout="vertical"
          onFinish={(values) => changeEmployment.mutate(values)}
        >
          <Form.Item
            name="reason"
            label="原因"
            rules={[{ required: true }, { min: 2 }, { max: 200 }]}
          >
            <Input.TextArea maxLength={200} showCount rows={4} />
          </Form.Item>
        </Form>
      </Modal>

      <Modal
        title="重置员工初始密码"
        open={passwordOpen}
        onCancel={() => setPasswordOpen(false)}
        onOk={() => passwordForm.submit()}
        okText="确认重置"
        confirmLoading={resetPassword.isPending}
        destroyOnHidden
      >
        <Alert
          type="warning"
          showIcon
          title="新密码只在本次输入，不会回显、保存到浏览器或写入审计日志。重置后现有登录会话失效。"
          className="modal-alert"
        />
        <Form<ResetPasswordValues>
          form={passwordForm}
          layout="vertical"
          onFinish={(values) => resetPassword.mutate(values)}
        >
          <Form.Item
            name="newInitialPassword"
            label="新初始密码"
            extra={PASSWORD_POLICY_HINT}
            rules={passwordRules("请输入新初始密码")}
          >
            <Input.Password maxLength={256} autoComplete="new-password" />
          </Form.Item>
          <Form.Item
            name="reason"
            label="重置原因"
            rules={[{ required: true }, { min: 2 }, { max: 200 }]}
          >
            <Input.TextArea maxLength={200} showCount rows={3} />
          </Form.Item>
        </Form>
      </Modal>

      <Drawer
        title="员工与账号详情"
      size={640}
        open={Boolean(selected)}
        onClose={() => setSelected(undefined)}
      >
        {selected && (
          <Space orientation="vertical" size={18} className="full-width">
            <Descriptions
              bordered
              size="small"
              column={1}
              items={[
                {
                  key: "name",
                  label: "员工",
                  children: `${selected.displayName} · ${selected.employeeNo}`,
                },
                {
                  key: "employment",
                  label: "在职状态",
                  children: (
                    <Tag
                      color={selected.status === "Active" ? "green" : "default"}
                    >
                      {selected.status === "Active" ? "在职" : "已离职"}
                    </Tag>
                  ),
                },
                {
                  key: "position",
                  label: "岗位",
                  children:
                    positionOptions.find(
                      (item) => item.value === selected.positionCode,
                    )?.label ?? selected.positionCode,
                },
                {
                  key: "store",
                  label: "所属门店",
                  children: selected.stores.map((store) => (
                    <Tag key={store.id}>
                      {store.name}
                      {store.isPrimary ? "（主）" : ""}
                    </Tag>
                  )),
                },
                {
                  key: "account",
                  label: "登录账号",
                  children: selected.account ?? "未开通",
                },
                {
                  key: "state",
                  label: "账号状态",
                  children: selected.account ? (
                    <Tag color={selected.accountEnabled ? "green" : "default"}>
                      {selected.accountEnabled ? "可登录" : "已停用"}
                    </Tag>
                  ) : (
                    "不适用"
                  ),
                },
                {
                  key: "roles",
                  label: "角色",
                  children: selected.roles.length
                    ? selected.roles.map((role) => (
                        <Tag key={role} color={roleColor[role]}>
                          {roleLabel(role)}
                        </Tag>
                      ))
                    : "无",
                },
                {
                  key: "password",
                  label: "首次改密",
                  children:
                    selected.mustChangePassword === undefined ? (
                      "不适用"
                    ) : selected.mustChangePassword ? (
                      <Tag color="gold">待完成</Tag>
                    ) : (
                      <Tag color="green">已完成</Tag>
                    ),
                },
              ]}
            />
            <Space wrap>
              <Button
                icon={<EditOutlined />}
                onClick={openEdit}
                disabled={selected.status !== "Active"}
              >
                编辑资料与权限
              </Button>
              <Button
                danger={selected.status === "Active"}
                onClick={() => setEmploymentOpen(true)}
                disabled={isSelf}
              >
                {selected.status === "Active" ? "办理离职" : "恢复在职"}
              </Button>
              {selected.account && (
                <Button
                  icon={<KeyOutlined />}
                  onClick={() => setPasswordOpen(true)}
                  disabled={isSelf || selected.status !== "Active"}
                >
                  重置密码
                </Button>
              )}
              {selected.account && (
                <Popconfirm
                  title={
                    selected.accountEnabled
                      ? "确认停用该登录账号？"
                      : "确认重新启用该登录账号？"
                  }
                  description="员工档案和历史业务不会被删除。"
                  okText="确认"
                  cancelText="取消"
                  onConfirm={() =>
                    setAccountStatus.mutate({
                      employee: selected,
                      isEnabled: !selected.accountEnabled,
                    })
                  }
                >
                  <Button
                    danger={selected.accountEnabled}
                    icon={
                      selected.accountEnabled ? (
                        <StopOutlined />
                      ) : (
                        <SafetyCertificateOutlined />
                      )
                    }
                    loading={setAccountStatus.isPending}
                    disabled={
                      isSelf ||
                      (selected.status !== "Active" && !selected.accountEnabled)
                    }
                  >
                    {selected.accountEnabled ? "停用账号" : "启用账号"}
                  </Button>
                </Popconfirm>
              )}
            </Space>
            {isSelf && (
              <Alert
                type="warning"
                showIcon
                title="为避免当前会话失去控制，不能修改自己的权限范围、在职状态、账号状态或重置自己的密码。"
              />
            )}
            <Alert
              type="info"
              showIcon
              title="角色与门店范围决定可访问的数据和动作；前端隐藏按钮不能替代服务端鉴权。"
            />
          </Space>
        )}
      </Drawer>
    </div>
  );
}

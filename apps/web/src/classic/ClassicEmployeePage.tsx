import {
  DeleteOutlined,
  EditOutlined,
  FileAddOutlined,
  KeyOutlined,
  ReloadOutlined,
  SearchOutlined,
  SettingOutlined,
  StopOutlined,
  UserOutlined,
} from "@ant-design/icons";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  Button,
  Checkbox,
  Empty,
  Form,
  Input,
  InputNumber,
  Modal,
  Pagination,
  Popconfirm,
  Select,
  Space,
  Spin,
  Table,
  Tag,
  message,
} from "antd";
import { useEffect, useMemo, useState } from "react";
import { useNavigate } from "react-router-dom";
import { apiRequest, ApiError } from "../api/client";
import type {
  Employee,
  EmployeePosition,
  EmployeeRole,
  PageResult,
} from "../api/types";
import { useAuth } from "../auth/useAuth";
import { useDebouncedValue } from "../hooks/useDebouncedValue";
import {
  PASSWORD_POLICY_HINT,
  passwordRules,
} from "../security/passwordPolicy";

interface EmployeeValues {
  displayName: string;
  positionCode: string;
  storeIds: string[];
  createLoginAccount: boolean;
  account?: string;
  initialPassword?: string;
  roles?: string[];
}
interface EditEmployeeValues {
  displayName: string;
  positionCode: string;
  storeIds: string[];
  roles: string[];
}
interface PositionValues {
  name: string;
  sortOrder: number;
  isEnabled: boolean;
}

const requestError = (error: unknown) =>
  error instanceof ApiError ? error.message : "操作失败";

export function ClassicEmployeePage() {
  const auth = useAuth();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [queryText, setQueryText] = useState("");
  const query = useDebouncedValue(queryText.trim());
  const [positionFilter, setPositionFilter] = useState<string>();
  const [page, setPage] = useState(1);
  const pageSize = 40;
  const [selected, setSelected] = useState<Employee>();
  const [createOpen, setCreateOpen] = useState(false);
  const [editOpen, setEditOpen] = useState(false);
  const [positionsOpen, setPositionsOpen] = useState(false);
  const [positionEditorOpen, setPositionEditorOpen] = useState(false);
  const [selectedPosition, setSelectedPosition] = useState<EmployeePosition>();
  const [employmentOpen, setEmploymentOpen] = useState(false);
  const [passwordOpen, setPasswordOpen] = useState(false);
  const [createForm] = Form.useForm<EmployeeValues>();
  const [editForm] = Form.useForm<EditEmployeeValues>();
  const [positionForm] = Form.useForm<PositionValues>();
  const [employmentForm] = Form.useForm<{ reason: string }>();
  const [passwordForm] = Form.useForm<{
    newInitialPassword: string;
    reason: string;
  }>();
  const createLogin = Form.useWatch("createLoginAccount", createForm);

  useEffect(() => setPage(1), [query, positionFilter]);
  const searchTerm = [query, positionFilter].filter(Boolean).join(" ");
  const params = new URLSearchParams({
    page: String(page),
    pageSize: String(pageSize),
  });
  if (searchTerm) params.set("query", searchTerm);
  const employees = useQuery({
    queryKey: ["classic-employees", searchTerm, page],
    queryFn: ({ signal }) =>
      apiRequest<PageResult<Employee>>(`/api/v1/employees?${params}`, {
        signal,
      }),
  });
  const positions = useQuery({
    queryKey: ["employee-positions"],
    queryFn: () =>
      apiRequest<EmployeePosition[]>("/api/v1/employees/positions"),
  });
  const roles = useQuery({
    queryKey: ["employee-roles"],
    queryFn: () => apiRequest<EmployeeRole[]>("/api/v1/employees/roles"),
  });
  const enabledPositions = useMemo(
    () => (positions.data ?? []).filter((item) => item.status === "ENABLED"),
    [positions.data],
  );
  const positionName = (code: string) =>
    positions.data?.find((item) => item.code === code)?.name ?? code;
  const roleName = (code: string) =>
    roles.data?.find((item) => item.code === code)?.name ?? code;
  const storeOptions =
    auth.user?.stores.map((item) => ({
      value: item.id,
      label: `${item.name} · ${item.code}`,
    })) ?? [];
  const positionOptions = enabledPositions.map((item) => ({
    value: item.code,
    label: `${item.name} · ${item.code}`,
  }));
  const allPositionOptions = (positions.data ?? []).map((item) => ({
    value: item.code,
    label: `${item.name} · ${item.code}`,
    disabled: item.status !== "ENABLED" && item.code !== selected?.positionCode,
  }));
  const roleOptions =
    roles.data?.map((item) => ({
      value: item.code,
      label: `${item.name} · ${item.code}`,
    })) ?? [];
  const onError = (error: unknown) => message.error(requestError(error));
  const refresh = () =>
    Promise.all([
      queryClient.invalidateQueries({ queryKey: ["classic-employees"] }),
      queryClient.invalidateQueries({ queryKey: ["employees"] }),
      queryClient.invalidateQueries({ queryKey: ["employee-positions"] }),
    ]);

  const createEmployee = useMutation({
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
    onSuccess: async (item) => {
      message.success(`员工已创建，工号 ${item.employeeNo}`);
      setCreateOpen(false);
      createForm.resetFields();
      setSelected(item);
      await refresh();
    },
    onError,
  });
  const updateEmployee = useMutation({
    mutationFn: (values: EditEmployeeValues) =>
      apiRequest<Employee>(`/api/v1/employees/${selected!.id}`, {
        method: "PUT",
        body: JSON.stringify({ ...values, expectedVersion: selected!.version }),
      }),
    onSuccess: async (item) => {
      message.success("员工资料已更新");
      setEditOpen(false);
      setSelected(item);
      await refresh();
    },
    onError,
  });
  const changeEmployment = useMutation({
    mutationFn: (values: { reason: string }) =>
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
    onSuccess: async (item) => {
      message.success(
        item.status === "Active"
          ? "员工已恢复在职"
          : "员工已离职，历史业务仍保留",
      );
      setEmploymentOpen(false);
      setSelected(item);
      employmentForm.resetFields();
      await refresh();
    },
    onError,
  });
  const setAccountStatus = useMutation({
    mutationFn: ({ item, enabled }: { item: Employee; enabled: boolean }) =>
      apiRequest<Employee>(`/api/v1/employees/${item.id}/account-status`, {
        method: "POST",
        body: JSON.stringify({ isEnabled: enabled }),
      }),
    onSuccess: async (item) => {
      message.success(
        item.accountEnabled ? "登录账号已启用" : "登录账号已停用",
      );
      setSelected(item);
      await refresh();
    },
    onError,
  });
  const resetPassword = useMutation({
    mutationFn: (values: { newInitialPassword: string; reason: string }) =>
      apiRequest<Employee>(`/api/v1/employees/${selected!.id}/reset-password`, {
        method: "POST",
        body: JSON.stringify(values),
      }),
    onSuccess: async (item) => {
      message.success("密码已重置，下次登录必须修改");
      setPasswordOpen(false);
      passwordForm.resetFields();
      setSelected(item);
      await refresh();
    },
    onError,
  });
  const createPosition = useMutation({
    mutationFn: (values: PositionValues) =>
      apiRequest<EmployeePosition>("/api/v1/employees/positions", {
        method: "POST",
        body: JSON.stringify({
          name: values.name,
          sortOrder: values.sortOrder,
        }),
      }),
    onSuccess: async () => {
      message.success("岗位已新增");
      setPositionEditorOpen(false);
      positionForm.resetFields();
      await refresh();
    },
    onError,
  });
  const updatePosition = useMutation({
    mutationFn: (values: PositionValues) =>
      apiRequest<EmployeePosition>(
        `/api/v1/employees/positions/${selectedPosition!.id}`,
        {
          method: "PUT",
          body: JSON.stringify({
            ...values,
            expectedVersion: selectedPosition!.version,
          }),
        },
      ),
    onSuccess: async () => {
      message.success("岗位已更新");
      setPositionEditorOpen(false);
      setSelectedPosition(undefined);
      positionForm.resetFields();
      await refresh();
    },
    onError,
  });
  const deletePosition = useMutation({
    mutationFn: (item: EmployeePosition) =>
      apiRequest<void>(
        `/api/v1/employees/positions/${item.id}?expectedVersion=${item.version}`,
        { method: "DELETE" },
      ),
    onSuccess: async () => {
      message.success("岗位已删除");
      await refresh();
    },
    onError,
  });

  const openCreate = () => {
    createForm.setFieldsValue({
      createLoginAccount: false,
      storeIds: auth.store ? [auth.store.id] : [],
      positionCode: enabledPositions[0]?.code,
      roles: [],
    });
    setCreateOpen(true);
  };
  const openEdit = () => {
    if (!selected) return message.info("请先选择一位员工");
    editForm.setFieldsValue({
      displayName: selected.displayName,
      positionCode: selected.positionCode,
      storeIds: selected.stores.map((item) => item.id),
      roles: selected.roles,
    });
    setEditOpen(true);
  };
  const openCreatePosition = () => {
    setSelectedPosition(undefined);
    positionForm.setFieldsValue({ name: "", sortOrder: 100, isEnabled: true });
    setPositionEditorOpen(true);
  };
  const openEditPosition = (item: EmployeePosition) => {
    setSelectedPosition(item);
    positionForm.setFieldsValue({
      name: item.name,
      sortOrder: item.sortOrder,
      isEnabled: item.status === "ENABLED",
    });
    setPositionEditorOpen(true);
  };
  const isSelf = selected?.userId === auth.user?.id;
  const toolbar = [
    ["新增", <FileAddOutlined />, openCreate],
    ["修改", <EditOutlined />, openEdit],
    ["岗位设置", <SettingOutlined />, () => setPositionsOpen(true)],
    [
      "查询",
      <SearchOutlined />,
      () =>
        document
          .querySelector<HTMLInputElement>("input.classic-employee-search")
          ?.focus(),
    ],
    ["刷新", <ReloadOutlined />, () => void refresh()],
    ["退出", <StopOutlined />, () => navigate("/ui/new/employee")],
  ] as const;

  return (
    <div className="classic-customer-list-page classic-employee-page">
      <div className="classic-customer-toolbar">
        {toolbar.map(([label, icon, action]) => (
          <button key={label} type="button" onClick={action}>
            {icon}
            <span>{label}</span>
          </button>
        ))}
      </div>
      <section className="classic-customer-query classic-employee-query">
        <label>
          员工查询
          <Input
            className="classic-employee-search"
            allowClear
            prefix={<SearchOutlined />}
            value={queryText}
            onChange={(event) => setQueryText(event.target.value)}
            placeholder="输入姓名、工号、账号、岗位或门店自动检索"
          />
        </label>
        <span>岗位是品牌自定义业务字段；登录角色只负责系统权限。</span>
        <Button
          onClick={() => {
            setQueryText("");
            setPositionFilter(undefined);
          }}
        >
          清空条件
        </Button>
      </section>
      <div className="classic-customer-workspace">
        <aside className="classic-customer-card-tree">
          <h3>员工岗位</h3>
          <button
            type="button"
            className={!positionFilter ? "active" : ""}
            onClick={() => setPositionFilter(undefined)}
          >
            全部岗位
          </button>
          {enabledPositions.map((item) => (
            <button
              key={item.id}
              type="button"
              className={positionFilter === item.name ? "active" : ""}
              onClick={() => setPositionFilter(item.name)}
            >
              {item.name}
              <small>{item.code}</small>
            </button>
          ))}
          <button
            type="button"
            className="classic-tree-setting"
            onClick={() => setPositionsOpen(true)}
          >
            <SettingOutlined /> 管理岗位
          </button>
        </aside>
        <section className="classic-customer-grid">
          <div className="classic-customer-table-scroll">
            <table>
              <thead>
                <tr>
                  {[
                    "员工工号",
                    "员工姓名",
                    "岗位",
                    "在职状态",
                    "所属门店",
                    "登录账号",
                    "账号状态",
                    "权限角色",
                    "登记时间",
                  ].map((label) => (
                    <th key={label}>{label}</th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {employees.isLoading && (
                  <tr>
                    <td colSpan={9}>
                      <Spin size="small" />
                    </td>
                  </tr>
                )}
                {!employees.isLoading && !employees.data?.items.length && (
                  <tr>
                    <td colSpan={9}>
                      <Empty
                        image={Empty.PRESENTED_IMAGE_SIMPLE}
                        description="没有符合条件的员工"
                      />
                    </td>
                  </tr>
                )}
                {employees.data?.items.map((item) => (
                  <tr
                    key={item.id}
                    className={selected?.id === item.id ? "selected" : ""}
                    onClick={() => setSelected(item)}
                    onDoubleClick={() => {
                      setSelected(item);
                      editForm.setFieldsValue({
                        displayName: item.displayName,
                        positionCode: item.positionCode,
                        storeIds: item.stores.map((store) => store.id),
                        roles: item.roles,
                      });
                      setEditOpen(true);
                    }}
                  >
                    <td>{item.employeeNo}</td>
                    <td>
                      <UserOutlined /> {item.displayName}
                    </td>
                    <td>{positionName(item.positionCode)}</td>
                    <td>{item.status === "Active" ? "在职" : "已离职"}</td>
                    <td>
                      {item.stores.map((store) => store.name).join("、") || "—"}
                    </td>
                    <td>{item.account ?? "未开通"}</td>
                    <td>
                      {item.account
                        ? item.accountEnabled
                          ? "可登录"
                          : "已停用"
                        : "—"}
                    </td>
                    <td>{item.roles.map(roleName).join("、") || "无"}</td>
                    <td>
                      {new Date(item.createdAtUtc).toLocaleDateString("zh-CN")}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <footer>
            <span>
              {selected
                ? `已选择：${selected.displayName} · ${positionName(selected.positionCode)}`
                : "员工档案与经典界面独立呈现，不跳转现代版页面"}
            </span>
            <Space>
              {selected && (
                <>
                  <Button size="small" onClick={() => setEmploymentOpen(true)}>
                    {selected.status === "Active" ? "办理离职" : "恢复在职"}
                  </Button>
                  {selected.account && (
                    <Button
                      size="small"
                      disabled={isSelf}
                      onClick={() =>
                        setAccountStatus.mutate({
                          item: selected,
                          enabled: !selected.accountEnabled,
                        })
                      }
                    >
                      {selected.accountEnabled ? "停用账号" : "启用账号"}
                    </Button>
                  )}
                  <Button
                    size="small"
                    icon={<KeyOutlined />}
                    disabled={!selected.account}
                    onClick={() => setPasswordOpen(true)}
                  >
                    重置密码
                  </Button>
                </>
              )}
            </Space>
            <Pagination
              size="small"
              current={page}
              pageSize={pageSize}
              total={employees.data?.total ?? 0}
              showSizeChanger={false}
              showTotal={(total) => `共 ${total} 人`}
              onChange={setPage}
            />
          </footer>
        </section>
      </div>

      <Modal
        title="新增员工"
        width={700}
        open={createOpen}
        onCancel={() => setCreateOpen(false)}
        onOk={() => createForm.submit()}
        okText="保存"
        confirmLoading={createEmployee.isPending}
        destroyOnHidden
      >
        <Form<EmployeeValues>
          form={createForm}
          layout="vertical"
          onFinish={(values) => createEmployee.mutate(values)}
          className="classic-employee-form"
        >
          <Form.Item
            name="displayName"
            label="员工姓名"
            rules={[{ required: true }, { min: 2 }, { max: 100 }]}
          >
            <Input />
          </Form.Item>
          <Form.Item
            name="positionCode"
            label="员工岗位"
            rules={[{ required: true }]}
          >
            <Select options={positionOptions} />
          </Form.Item>
          <Form.Item
            name="storeIds"
            label="所属门店"
            rules={[{ required: true }]}
          >
            <Select mode="multiple" options={storeOptions} />
          </Form.Item>
          <Form.Item name="createLoginAccount" valuePropName="checked">
            <Checkbox>同时开通登录账号</Checkbox>
          </Form.Item>
          {createLogin && (
            <>
              <Form.Item
                name="account"
                label="登录账号"
                rules={[{ required: true }, { min: 3 }, { max: 64 }]}
              >
                <Input />
              </Form.Item>
              <Form.Item
                name="initialPassword"
                label="初始密码"
                extra={PASSWORD_POLICY_HINT}
                rules={passwordRules("请输入初始密码")}
              >
                <Input.Password />
              </Form.Item>
              <Form.Item
                name="roles"
                label="权限角色"
                rules={[{ required: true }]}
              >
                <Select mode="multiple" options={roleOptions} />
              </Form.Item>
            </>
          )}
        </Form>
      </Modal>
      <Modal
        title="修改员工"
        width={680}
        open={editOpen}
        onCancel={() => setEditOpen(false)}
        onOk={() => editForm.submit()}
        okText="保存"
        confirmLoading={updateEmployee.isPending}
        destroyOnHidden
      >
        <Form<EditEmployeeValues>
          form={editForm}
          layout="vertical"
          onFinish={(values) => updateEmployee.mutate(values)}
          className="classic-employee-form"
        >
          <Form.Item
            name="displayName"
            label="员工姓名"
            rules={[{ required: true }, { min: 2 }, { max: 100 }]}
          >
            <Input />
          </Form.Item>
          <Form.Item
            name="positionCode"
            label="员工岗位"
            rules={[{ required: true }]}
          >
            <Select options={allPositionOptions} />
          </Form.Item>
          <Form.Item
            name="storeIds"
            label="所属门店"
            rules={[{ required: true }]}
          >
            <Select mode="multiple" options={storeOptions} />
          </Form.Item>
          <Form.Item name="roles" label="权限角色">
            <Select mode="multiple" options={roleOptions} />
          </Form.Item>
        </Form>
      </Modal>
      <Modal
        title="岗位设置"
        width={760}
        open={positionsOpen}
        onCancel={() => setPositionsOpen(false)}
        footer={<Button onClick={() => setPositionsOpen(false)}>关闭</Button>}
        destroyOnHidden
      >
        <Space style={{ marginBottom: 10 }}>
          <Button
            type="primary"
            icon={<FileAddOutlined />}
            onClick={openCreatePosition}
          >
            新增岗位
          </Button>
          <span>岗位名称可自定义，编码自动生成且改名不变。</span>
        </Space>
        <Table<EmployeePosition>
          rowKey="id"
          size="small"
          pagination={false}
          loading={positions.isFetching}
          dataSource={positions.data}
          columns={[
            { title: "岗位编码", dataIndex: "code", width: 150 },
            { title: "岗位名称", dataIndex: "name" },
            { title: "排序", dataIndex: "sortOrder", width: 80 },
            {
              title: "状态",
              dataIndex: "status",
              width: 80,
              render: (value: string) => (
                <Tag color={value === "ENABLED" ? "green" : "default"}>
                  {value === "ENABLED" ? "启用" : "停用"}
                </Tag>
              ),
            },
            {
              title: "操作",
              key: "action",
              width: 160,
              render: (_value, item) => (
                <Space>
                  <Button
                    size="small"
                    icon={<EditOutlined />}
                    onClick={() => openEditPosition(item)}
                  >
                    修改
                  </Button>
                  <Popconfirm
                    title="确认删除该岗位？"
                    description="已被员工使用的岗位不能删除，可改为停用。"
                    onConfirm={() => deletePosition.mutate(item)}
                  >
                    <Button size="small" danger icon={<DeleteOutlined />}>
                      删除
                    </Button>
                  </Popconfirm>
                </Space>
              ),
            },
          ]}
        />
      </Modal>
      <Modal
        title={selectedPosition ? "修改岗位" : "新增岗位"}
        open={positionEditorOpen}
        onCancel={() => setPositionEditorOpen(false)}
        onOk={() => positionForm.submit()}
        okText="保存"
        confirmLoading={createPosition.isPending || updatePosition.isPending}
        destroyOnHidden
      >
        <Form<PositionValues>
          form={positionForm}
          layout="vertical"
          onFinish={(values) =>
            selectedPosition
              ? updatePosition.mutate(values)
              : createPosition.mutate(values)
          }
        >
          {selectedPosition && (
            <Form.Item label="岗位编码">
              <Input value={selectedPosition.code} disabled />
            </Form.Item>
          )}
          <Form.Item
            name="name"
            label="岗位名称"
            rules={[{ required: true }, { min: 2 }, { max: 60 }]}
          >
            <Input placeholder="例如：顾问、安装工程师、课程老师" />
          </Form.Item>
          <Form.Item
            name="sortOrder"
            label="显示顺序"
            rules={[{ required: true }]}
          >
            <InputNumber min={0} max={9999} style={{ width: "100%" }} />
          </Form.Item>
          {selectedPosition && (
            <Form.Item name="isEnabled" valuePropName="checked">
              <Checkbox>启用该岗位</Checkbox>
            </Form.Item>
          )}
        </Form>
      </Modal>
      <Modal
        title={selected?.status === "Active" ? "办理离职" : "恢复在职"}
        open={employmentOpen}
        onCancel={() => setEmploymentOpen(false)}
        onOk={() => employmentForm.submit()}
        okText="确定"
        confirmLoading={changeEmployment.isPending}
        destroyOnHidden
      >
        <Form
          form={employmentForm}
          layout="vertical"
          onFinish={(values) => changeEmployment.mutate(values)}
        >
          <Form.Item
            name="reason"
            label="原因"
            rules={[{ required: true }, { min: 2 }, { max: 500 }]}
          >
            <Input.TextArea rows={3} />
          </Form.Item>
        </Form>
      </Modal>
      <Modal
        title="重置员工密码"
        open={passwordOpen}
        onCancel={() => setPasswordOpen(false)}
        onOk={() => passwordForm.submit()}
        okText="确定重置"
        confirmLoading={resetPassword.isPending}
        destroyOnHidden
      >
        <Form
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
            <Input.Password />
          </Form.Item>
          <Form.Item
            name="reason"
            label="重置原因"
            rules={[{ required: true }, { min: 4 }, { max: 200 }]}
          >
            <Input.TextArea rows={2} />
          </Form.Item>
        </Form>
      </Modal>
    </div>
  );
}

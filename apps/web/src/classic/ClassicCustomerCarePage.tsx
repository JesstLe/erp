import {
  DeleteOutlined,
  EditOutlined,
  FileAddOutlined,
  FileImageOutlined,
  PrinterOutlined,
  ReloadOutlined,
  SearchOutlined,
  SettingOutlined,
  StopOutlined,
  TableOutlined,
  UploadOutlined,
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
  Upload,
  message,
  type UploadFile,
} from "antd";
import { useEffect, useMemo, useState } from "react";
import { useNavigate } from "react-router-dom";
import { apiRequest, ApiError } from "../api/client";
import type {
  CustomerSummary,
  PageResult,
  ServiceRecord,
  ServiceRecordCategory,
  ServiceRecordOverview,
} from "../api/types";
import { useAuth } from "../auth/useAuth";
import { useDebouncedValue } from "../hooks/useDebouncedValue";

interface CareRecordValues {
  customerId: string;
  categoryId?: string;
  serviceOccurredAt: string;
  conditionNotes?: string;
  serviceContent?: string;
  followUpNotes?: string;
}

interface CategoryValues {
  name: string;
  sortOrder: number;
  isEnabled: boolean;
}

interface CorrectionValues {
  reason: string;
  conditionNotes?: string;
  serviceContent?: string;
  followUpNotes?: string;
}

const commandId = () => crypto.randomUUID();
const localDateTimeValue = () => {
  const value = new Date(Date.now() - new Date().getTimezoneOffset() * 60_000);
  return value.toISOString().slice(0, 16);
};
const formatTime = (value: string) =>
  new Date(value).toLocaleString("zh-CN", { hour12: false });
const requestError = (error: unknown) =>
  error instanceof ApiError ? error.message : "操作失败";

export function ClassicCustomerCarePage() {
  const auth = useAuth();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const storeId = auth.store?.id;
  const [queryText, setQueryText] = useState("");
  const query = useDebouncedValue(queryText.trim());
  const [categoryId, setCategoryId] = useState<string>();
  const [page, setPage] = useState(1);
  const pageSize = 40;
  const [selected, setSelected] = useState<ServiceRecordOverview>();
  const [compact, setCompact] = useState(false);
  const [createOpen, setCreateOpen] = useState(false);
  const [correctionOpen, setCorrectionOpen] = useState(false);
  const [categoryManagerOpen, setCategoryManagerOpen] = useState(false);
  const [categoryEditorOpen, setCategoryEditorOpen] = useState(false);
  const [selectedCategory, setSelectedCategory] =
    useState<ServiceRecordCategory>();
  const [customerQuery, setCustomerQuery] = useState("");
  const customerSearch = useDebouncedValue(customerQuery.trim());
  const [images, setImages] = useState<UploadFile[]>([]);
  const [recordForm] = Form.useForm<CareRecordValues>();
  const [categoryForm] = Form.useForm<CategoryValues>();
  const [correctionForm] = Form.useForm<CorrectionValues>();

  useEffect(() => setPage(1), [storeId, query, categoryId]);
  const categories = useQuery({
    queryKey: ["service-record-categories"],
    queryFn: () =>
      apiRequest<ServiceRecordCategory[]>(
        "/api/v1/customers/service-record-categories",
      ),
  });
  const overviewParams = new URLSearchParams({
    storeId: storeId ?? "",
    page: String(page),
    pageSize: String(pageSize),
  });
  if (categoryId) overviewParams.set("categoryId", categoryId);
  if (query) overviewParams.set("query", query);
  const records = useQuery({
    queryKey: [
      "classic-service-record-overview",
      storeId,
      categoryId,
      query,
      page,
    ],
    enabled: Boolean(storeId),
    queryFn: ({ signal }) =>
      apiRequest<PageResult<ServiceRecordOverview>>(
        `/api/v1/customers/service-record-overview?${overviewParams}`,
        { signal },
      ),
  });
  const customers = useQuery({
    queryKey: ["classic-care-customer-search", storeId, customerSearch],
    enabled: Boolean(storeId && createOpen),
    queryFn: ({ signal }) =>
      apiRequest<PageResult<CustomerSummary>>("/api/v1/customers/search", {
        method: "POST",
        body: JSON.stringify({
          storeId,
          query: customerSearch,
          page: 1,
          pageSize: 30,
        }),
        signal,
      }),
  });

  const refresh = async () => {
    await Promise.all([
      queryClient.invalidateQueries({
        queryKey: ["classic-service-record-overview", storeId],
      }),
      queryClient.invalidateQueries({
        queryKey: ["service-record-categories"],
      }),
    ]);
  };
  const onError = (error: unknown) => message.error(requestError(error));
  const createRecord = useMutation({
    mutationFn: async (values: CareRecordValues) => {
      const data = new FormData();
      data.append("storeId", storeId!);
      data.append("commandId", commandId());
      data.append(
        "serviceOccurredAtUtc",
        new Date(values.serviceOccurredAt).toISOString(),
      );
      if (values.categoryId) data.append("categoryId", values.categoryId);
      if (values.conditionNotes?.trim())
        data.append("conditionNotes", values.conditionNotes.trim());
      if (values.serviceContent?.trim())
        data.append("serviceContent", values.serviceContent.trim());
      if (values.followUpNotes?.trim())
        data.append("followUpNotes", values.followUpNotes.trim());
      images.forEach((image) => {
        if (image.originFileObj) data.append("images", image.originFileObj);
      });
      return apiRequest<ServiceRecord>(
        `/api/v1/customers/${values.customerId}/service-records`,
        { method: "POST", body: data },
      );
    },
    onSuccess: async () => {
      message.success("服务档案已保存");
      setCreateOpen(false);
      setImages([]);
      recordForm.resetFields();
      setCustomerQuery("");
      await refresh();
    },
    onError,
  });
  const correctRecord = useMutation({
    mutationFn: (values: CorrectionValues) =>
      apiRequest<ServiceRecord>(
        `/api/v1/customers/${selected!.customerId}/service-records/${selected!.id}/corrections`,
        {
          method: "POST",
          body: JSON.stringify({ ...values, storeId, commandId: commandId() }),
        },
      ),
    onSuccess: async () => {
      message.success("更正内容已追加，原始记录保持不变");
      setCorrectionOpen(false);
      correctionForm.resetFields();
      await refresh();
    },
    onError,
  });
  const createCategory = useMutation({
    mutationFn: (values: CategoryValues) =>
      apiRequest<ServiceRecordCategory>(
        "/api/v1/customers/service-record-categories",
        {
          method: "POST",
          body: JSON.stringify({
            name: values.name,
            sortOrder: values.sortOrder,
          }),
        },
      ),
    onSuccess: async () => {
      message.success("分类已新增");
      setCategoryEditorOpen(false);
      categoryForm.resetFields();
      await refresh();
    },
    onError,
  });
  const updateCategory = useMutation({
    mutationFn: (values: CategoryValues) =>
      apiRequest<ServiceRecordCategory>(
        `/api/v1/customers/service-record-categories/${selectedCategory!.id}`,
        {
          method: "PUT",
          body: JSON.stringify({
            ...values,
            expectedVersion: selectedCategory!.version,
          }),
        },
      ),
    onSuccess: async () => {
      message.success("分类已更新");
      setCategoryEditorOpen(false);
      setSelectedCategory(undefined);
      categoryForm.resetFields();
      await refresh();
    },
    onError,
  });
  const deleteCategory = useMutation({
    mutationFn: (item: ServiceRecordCategory) =>
      apiRequest<void>(
        `/api/v1/customers/service-record-categories/${item.id}?expectedVersion=${item.version}`,
        { method: "DELETE" },
      ),
    onSuccess: async () => {
      message.success("分类已删除");
      if (categoryId === selectedCategory?.id) setCategoryId(undefined);
      await refresh();
    },
    onError,
  });

  const enabledCategories = useMemo(
    () => (categories.data ?? []).filter((item) => item.status === "ENABLED"),
    [categories.data],
  );
  const openCreate = () => {
    recordForm.setFieldsValue({
      serviceOccurredAt: localDateTimeValue(),
      categoryId,
    });
    setCreateOpen(true);
  };
  const openCreateCategory = () => {
    setSelectedCategory(undefined);
    categoryForm.setFieldsValue({ name: "", sortOrder: 100, isEnabled: true });
    setCategoryEditorOpen(true);
  };
  const openEditCategory = (item: ServiceRecordCategory) => {
    setSelectedCategory(item);
    categoryForm.setFieldsValue({
      name: item.name,
      sortOrder: item.sortOrder,
      isEnabled: item.status === "ENABLED",
    });
    setCategoryEditorOpen(true);
  };
  const openCorrection = (record = selected) => {
    if (!record) return message.info("请先选择一条服务记录");
    setSelected(record);
    correctionForm.setFieldsValue({
      reason: "",
      conditionNotes: record.conditionNotes,
      serviceContent: record.serviceContent,
      followUpNotes: record.followUpNotes,
    });
    setCorrectionOpen(true);
  };
  const chooseImage = (file: UploadFile) => {
    if ((file.size ?? 0) > 5 * 1024 * 1024) {
      message.error("单张图片不能超过5MB");
      return Upload.LIST_IGNORE;
    }
    if (images.length >= 6) {
      message.error("最多上传6张图片");
      return Upload.LIST_IGNORE;
    }
    setImages((current) => [...current, file]);
    return false;
  };

  const toolbar = [
    ["新增", <FileAddOutlined />, openCreate],
    ["更正", <EditOutlined />, () => openCorrection()],
    ["分类设置", <SettingOutlined />, () => setCategoryManagerOpen(true)],
    [
      "查询",
      <SearchOutlined />,
      () =>
        document
          .querySelector<HTMLInputElement>("input.classic-care-search")
          ?.focus(),
    ],
    ["刷新", <ReloadOutlined />, () => void refresh()],
    ["表格", <TableOutlined />, () => setCompact((value) => !value)],
    ["打印", <PrinterOutlined />, () => window.print()],
    ["退出", <StopOutlined />, () => navigate("/ui/new/customer")],
  ] as const;

  return (
    <div className="classic-customer-list-page classic-care-page">
      <div className="classic-customer-toolbar">
        {toolbar.map(([label, icon, action]) => (
          <button key={label} type="button" onClick={action}>
            {icon}
            <span>{label}</span>
          </button>
        ))}
      </div>
      <section className="classic-customer-query classic-care-query">
        <label>
          顾客/手机号查询
          <Input
            className="classic-care-search"
            allowClear
            prefix={<SearchOutlined />}
            value={queryText}
            onChange={(event) => setQueryText(event.target.value)}
            placeholder="输入姓名、完整手机号或尾号自动检索"
          />
        </label>
        <span>输入后自动加载，无需点击查询</span>
        <Button
          onClick={() => {
            setQueryText("");
            setCategoryId(undefined);
          }}
        >
          清空条件
        </Button>
      </section>
      <div className="classic-customer-workspace">
        <aside className="classic-customer-card-tree">
          <h3>服务记录分类</h3>
          <button
            type="button"
            className={!categoryId ? "active" : ""}
            onClick={() => setCategoryId(undefined)}
          >
            全部记录
          </button>
          <button
            type="button"
            className={categoryId === "uncategorized" ? "active" : ""}
            disabled
          >
            未分类（可在新增时选择）
          </button>
          {enabledCategories.map((item) => (
            <button
              key={item.id}
              type="button"
              className={categoryId === item.id ? "active" : ""}
              onClick={() => setCategoryId(item.id)}
            >
              {item.name}
              <small>{item.code}</small>
            </button>
          ))}
          <button
            type="button"
            className="classic-tree-setting"
            onClick={() => setCategoryManagerOpen(true)}
          >
            <SettingOutlined /> 管理分类
          </button>
        </aside>
        <section
          className={`classic-customer-grid ${compact ? "is-compact" : ""}`}
        >
          <div className="classic-customer-table-scroll">
            <table>
              <thead>
                <tr>
                  {[
                    "服务时间",
                    "顾客姓名",
                    "手机号",
                    "分类",
                    "本次情况/需求",
                    "服务过程与内容",
                    "结果与后续建议",
                    "关联消费单",
                    "图片",
                    "更正",
                    "记录人",
                    "建档时间",
                  ].map((label) => (
                    <th key={label}>{label}</th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {records.isLoading && (
                  <tr>
                    <td colSpan={12}>
                      <Spin size="small" />
                    </td>
                  </tr>
                )}
                {!records.isLoading && !records.data?.items.length && (
                  <tr>
                    <td colSpan={12}>
                      <Empty
                        image={Empty.PRESENTED_IMAGE_SIMPLE}
                        description="当前条件下暂无服务记录"
                      />
                    </td>
                  </tr>
                )}
                {records.data?.items.map((item) => (
                  <tr
                    key={item.id}
                    className={selected?.id === item.id ? "selected" : ""}
                    onClick={() => setSelected(item)}
                    onDoubleClick={() => openCorrection(item)}
                  >
                    <td>{formatTime(item.serviceOccurredAtUtc)}</td>
                    <td>{item.customerName}</td>
                    <td>{item.maskedMobile}</td>
                    <td>{item.categoryName ?? "未分类"}</td>
                    <td title={item.conditionNotes}>
                      {item.conditionNotes ?? "—"}
                    </td>
                    <td title={item.serviceContent}>
                      {item.serviceContent ?? "—"}
                    </td>
                    <td title={item.followUpNotes}>
                      {item.followUpNotes ?? "—"}
                    </td>
                    <td>{item.serviceOrderNo ?? "—"}</td>
                    <td>{item.attachmentCount}</td>
                    <td>{item.correctionCount}</td>
                    <td>{item.createdByName}</td>
                    <td>{formatTime(item.createdAtUtc)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <footer>
            <span>
              {selected
                ? `已选择：${selected.customerName} · ${selected.categoryName ?? "未分类"}`
                : "服务档案按所选门店展示，分类在品牌内共用"}
            </span>
            <Pagination
              size="small"
              current={page}
              pageSize={pageSize}
              total={records.data?.total ?? 0}
              showSizeChanger={false}
              showTotal={(total) => `共 ${total} 条`}
              onChange={setPage}
            />
          </footer>
        </section>
      </div>

      <Modal
        title="新增服务档案"
        width={760}
        open={createOpen}
        onCancel={() => setCreateOpen(false)}
        onOk={() => recordForm.submit()}
        okText="保存档案"
        confirmLoading={createRecord.isPending}
        destroyOnHidden
      >
        <Form<CareRecordValues>
          form={recordForm}
          layout="vertical"
          onFinish={(values) => createRecord.mutate(values)}
          className="classic-care-record-form"
        >
          <Form.Item
            name="customerId"
            label="顾客"
            rules={[{ required: true, message: "请选择顾客" }]}
          >
            <Select
              showSearch
              filterOption={false}
              onSearch={setCustomerQuery}
              loading={customers.isFetching}
              placeholder="输入姓名、完整手机号或尾号自动查询"
              options={customers.data?.items.map((item) => ({
                value: item.id,
                label: `${item.displayName} · ${item.maskedMobile} · ${item.homeStoreName}`,
              }))}
            />
          </Form.Item>
          <Form.Item
            name="serviceOccurredAt"
            label="服务时间"
            rules={[{ required: true, message: "请选择服务时间" }]}
          >
            <Input type="datetime-local" max={localDateTimeValue()} />
          </Form.Item>
          <Form.Item name="categoryId" label="服务记录分类（可选）">
            <Select
              allowClear
              showSearch
              placeholder="未分类"
              options={enabledCategories.map((item) => ({
                value: item.id,
                label: `${item.name} · ${item.code}`,
              }))}
            />
          </Form.Item>
          <Form.Item
            name="conditionNotes"
            label="本次情况/需求（可选）"
            rules={[{ max: 2000 }]}
          >
            <Input.TextArea rows={3} maxLength={2000} showCount />
          </Form.Item>
          <Form.Item
            name="serviceContent"
            label="服务过程与内容（可选）"
            rules={[{ max: 4000 }]}
          >
            <Input.TextArea rows={4} maxLength={4000} showCount />
          </Form.Item>
          <Form.Item
            name="followUpNotes"
            label="结果与后续建议（可选）"
            rules={[{ max: 2000 }]}
          >
            <Input.TextArea rows={3} maxLength={2000} showCount />
          </Form.Item>
          <Form.Item label="服务图片（可选，最多6张）">
            <Upload
              accept="image/jpeg,image/png,image/webp"
              multiple
              listType="picture"
              fileList={images}
              beforeUpload={chooseImage}
              onRemove={(file) => {
                setImages((current) =>
                  current.filter((item) => item.uid !== file.uid),
                );
                return true;
              }}
            >
              <Button icon={<UploadOutlined />} disabled={images.length >= 6}>
                选择图片
              </Button>
            </Upload>
            <span className="classic-upload-note">
              <FileImageOutlined /> JPEG、PNG、WebP，单张不超过5MB
            </span>
          </Form.Item>
        </Form>
      </Modal>

      <Modal
        title="更正服务档案"
        width={700}
        open={correctionOpen}
        onCancel={() => setCorrectionOpen(false)}
        onOk={() => correctionForm.submit()}
        okText="追加更正"
        confirmLoading={correctRecord.isPending}
        destroyOnHidden
      >
        <p>服务档案不覆盖原文；本次修改会形成有操作人和时间的更正记录。</p>
        <Form<CorrectionValues>
          form={correctionForm}
          layout="vertical"
          onFinish={(values) => correctRecord.mutate(values)}
        >
          <Form.Item
            name="reason"
            label="更正原因"
            rules={[{ required: true }, { min: 2 }, { max: 500 }]}
          >
            <Input.TextArea rows={2} maxLength={500} />
          </Form.Item>
          <Form.Item
            name="conditionNotes"
            label="更正后的本次情况/需求（可选）"
          >
            <Input.TextArea rows={3} maxLength={2000} />
          </Form.Item>
          <Form.Item
            name="serviceContent"
            label="更正后的服务过程与内容（可选）"
          >
            <Input.TextArea rows={4} maxLength={4000} />
          </Form.Item>
          <Form.Item
            name="followUpNotes"
            label="更正后的结果与后续建议（可选）"
          >
            <Input.TextArea rows={3} maxLength={2000} />
          </Form.Item>
        </Form>
      </Modal>

      <Modal
        title="服务记录分类设置"
        width={760}
        open={categoryManagerOpen}
        onCancel={() => setCategoryManagerOpen(false)}
        footer={
          <Button onClick={() => setCategoryManagerOpen(false)}>关闭</Button>
        }
        destroyOnHidden
      >
        <Space style={{ marginBottom: 10 }}>
          <Button
            type="primary"
            icon={<FileAddOutlined />}
            onClick={openCreateCategory}
          >
            新增分类
          </Button>
          <span>名称由品牌自定义，编码由系统自动生成且改名不变。</span>
        </Space>
        <Table<ServiceRecordCategory>
          rowKey="id"
          size="small"
          pagination={false}
          loading={categories.isFetching}
          dataSource={categories.data}
          columns={[
            { title: "分类编码", dataIndex: "code", width: 150 },
            { title: "分类名称", dataIndex: "name" },
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
                    onClick={() => openEditCategory(item)}
                  >
                    修改
                  </Button>
                  <Popconfirm
                    title="确认删除该分类？"
                    description="已被服务记录使用的分类不能删除，可改为停用。"
                    onConfirm={() => deleteCategory.mutate(item)}
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
        title={selectedCategory ? "修改服务记录分类" : "新增服务记录分类"}
        open={categoryEditorOpen}
        onCancel={() => setCategoryEditorOpen(false)}
        onOk={() => categoryForm.submit()}
        okText="保存"
        confirmLoading={createCategory.isPending || updateCategory.isPending}
        destroyOnHidden
      >
        <Form<CategoryValues>
          form={categoryForm}
          layout="vertical"
          onFinish={(values) =>
            selectedCategory
              ? updateCategory.mutate(values)
              : createCategory.mutate(values)
          }
        >
          {selectedCategory && (
            <Form.Item label="分类编码">
              <Input value={selectedCategory.code} disabled />
            </Form.Item>
          )}
          <Form.Item
            name="name"
            label="分类名称"
            rules={[{ required: true }, { min: 1 }, { max: 60 }]}
          >
            <Input
              maxLength={60}
              placeholder="例如：售后回访、设备巡检、课程辅导"
            />
          </Form.Item>
          <Form.Item
            name="sortOrder"
            label="显示顺序"
            rules={[{ required: true }]}
          >
            <InputNumber min={0} max={9999} style={{ width: "100%" }} />
          </Form.Item>
          {selectedCategory && (
            <Form.Item name="isEnabled" valuePropName="checked">
              <Checkbox>启用该分类</Checkbox>
            </Form.Item>
          )}
        </Form>
      </Modal>
    </div>
  );
}

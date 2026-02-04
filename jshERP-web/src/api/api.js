import { getAction, deleteAction, putAction, postAction, httpAction } from '@/api/manage'

//首页统计
const getBuyAndSaleStatistics = (params)=>getAction("/depotHead/getBuyAndSaleStatistics",params);
const buyOrSalePrice = (params)=>getAction("/depotItem/buyOrSalePrice",params);
//租户管理
const checkTenant = (params)=>getAction("/tenant/checkIsNameExist",params);
const addTenant = (params)=>postAction("/tenant/add",params);
const editTenant = (params)=>putAction("/tenant/update",params);
//角色管理
const addRole = (params)=>postAction("/role/add",params);
const editRole = (params)=>putAction("/role/update",params);
const checkRole = (params)=>getAction("/role/checkIsNameExist",params);
const roleAllList = (params)=>getAction("/role/allList",params);
const getTenantRoleList = (params)=>getAction("/role/tenantRoleList",params);
//用户管理
const registerUser = (params)=>postAction("/user/registerUser",params);
const addUser = (params)=>postAction("/user/addUser",params);
const editUser = (params)=>putAction("/user/updateUser",params);
const getUserList = (params)=>getAction("/user/getUserList",params);
const getUserBtnByCurrentUser = (params)=>getAction("/user/getUserBtnByCurrentUser",params);
const queryPermissionsByUser = (params)=>postAction("/function/findMenuByPNumber",params);
//机构管理
const queryOrganizationTreeList = (params)=>getAction("/organization/getOrganizationTree",params);
const getAllOrganizationTreeByUser = (params)=>getAction("/organization/getAllOrganizationTreeByUser",params);
const queryOrganizationById = (params)=>getAction("/organization/findById",params);
const checkOrganization = (params)=>getAction("/organization/checkIsNameExist",params);
//经手人管理
const addPerson = (params)=>postAction("/person/add",params);
const editPerson = (params)=>putAction("/person/update",params);
const checkPerson = (params)=>getAction("/person/checkIsNameExist",params);
const getPersonByType = (params)=>getAction("/person/getPersonByType",params);
const getPersonByNumType = (params)=>getAction("/person/getPersonByNumType",params);
//账户管理
const addAccount = (params)=>postAction("/account/add",params);
const editAccount = (params)=>putAction("/account/update",params);
const checkAccount = (params)=>getAction("/account/checkIsNameExist",params);
const getAccount = (params)=>getAction("/account/getAccount",params);
//收支项目
const addInOutItem = (params)=>postAction("/inOutItem/add",params);
const editInOutItem = (params)=>putAction("/inOutItem/update",params);
const checkInOutItem = (params)=>getAction("/inOutItem/checkIsNameExist",params);
const findInOutItemByParam = (params)=>getAction("/inOutItem/findBySelect",params);
//仓库信息
const addDepot = (params)=>postAction("/depot/add",params);
const editDepot = (params)=>putAction("/depot/update",params);
const checkDepot = (params)=>getAction("/depot/checkIsNameExist",params);
//商品属性
const addOrUpdateMaterialProperty = (params)=>postAction("/materialProperty/addOrUpdate",params);
//商品类型
const queryMaterialCategoryTreeList = (params)=>getAction("/materialCategory/getMaterialCategoryTree",params);
const queryMaterialCategoryById = (params)=>getAction("/materialCategory/findById",params);
const checkMaterialCategory = (params)=>getAction("/materialCategory/checkIsNameExist",params);
//商品管理
const addMaterial = (params)=>postAction("/material/add",params);
const editMaterial = (params)=>putAction("/material/update",params);
const checkMaterial = (params)=>getAction("/material/checkIsExist",params);
const getMaterialBySelect = (params)=>getAction("/material/findBySelect",params);
const getSerialMaterialBySelect = (params)=>getAction("/material/getMaterialEnableSerialNumberList",params);
const getMaterialByParam = (params)=>getAction("/material/getMaterialByParam",params);
const getMaterialByBarCode = (params)=>getAction("/material/getMaterialByBarCode",params);
const getMaxBarCode = (params)=>getAction("/material/getMaxBarCode",params);
const checkMaterialBarCode = (params)=>getAction("/materialsExtend/checkIsBarCodeExist",params);
const batchUpdateMaterial = (params)=>postAction("/material/batchUpdate",params);
const changeNameToPinYin = (params)=>postAction("/material/changeNameToPinYin",params);
//序列号
const batAddSerialNumber = (params)=>postAction("/serialNumber/batAddSerialNumber",params);
const getEnableSerialNumberList = (params)=>postAction("/serialNumber/getEnableSerialNumberList",params);
//多属性
const addMaterialAttribute = (params)=>postAction("/materialAttribute/add",params);
const editMaterialAttribute = (params)=>putAction("/materialAttribute/update",params);
const checkMaterialAttribute = (params)=>getAction("/materialAttribute/checkIsNameExist",params);
const getMaterialAttributeNameList = (params)=>getAction("/materialAttribute/getNameList",params);
const getMaterialAttributeValueListById = (params)=>getAction("/materialAttribute/getValueListById",params);
//功能管理
const addFunction = (params)=>postAction("/function/add",params);
const editFunction = (params)=>putAction("/function/update",params);
const checkFunction = (params)=>getAction("/function/checkIsNameExist",params);
const checkNumber = (params)=>getAction("/function/checkIsNumberExist",params);
//系统配置
const addSystemConfig = (params)=>postAction("/systemConfig/add",params);
const editSystemConfig = (params)=>putAction("/systemConfig/update",params);
const checkSystemConfig = (params)=>getAction("/systemConfig/checkIsNameExist",params);
const getCurrentSystemConfig = (params)=>getAction("/systemConfig/getCurrentInfo",params);
const fileSizeLimit = (params)=>getAction("/systemConfig/fileSizeLimit",params);
//平台参数
const addPlatformConfig = (params)=>postAction("/platformConfig/add",params);
const editPlatformConfig = (params)=>putAction("/platformConfig/update",params);
const getPlatformConfigByKey = (params)=>getAction("/platformConfig/getInfoByKey",params);
//用户|角色|模块关系
const addUserBusiness = (params)=>postAction("/userBusiness/add",params);
const editUserBusiness = (params)=>putAction("/userBusiness/update",params);
const checkUserBusiness = (params)=>getAction("/userBusiness/checkIsValueExist",params);
const updateBtnStrByRoleId = (params)=>postAction("/userBusiness/updateBtnStr",params);
const updateOneValueByKeyIdAndType = (params)=>postAction("/userBusiness/updateOneValueByKeyIdAndType",params);
//多单位
const addUnit = (params)=>postAction("/unit/add",params);
const editUnit = (params)=>putAction("/unit/update",params);
const checkUnit = (params)=>getAction("/unit/checkIsNameExist",params);
//供应商|客户|会员
const addSupplier = (params)=>postAction("/supplier/add",params);
const editSupplier = (params)=>putAction("/supplier/update",params);
const checkSupplier = (params)=>getAction("/supplier/checkIsNameAndTypeExist",params);
const findBySelectSup = (params)=>postAction("/supplier/findBySelect_sup",params);
const findBySelectCus = (params)=>postAction("/supplier/findBySelect_cus",params);
const findBySelectRetail = (params)=>postAction("/supplier/findBySelect_retail",params);
const findBySelectOrgan = (params)=>postAction("/supplier/findBySelect_organ",params);
//单据相关
const findBillDetailByNumber = (params)=>getAction("/depotHead/getDetailByNumber",params);
const waitBillCount = (params)=>getAction("/depotHead/waitBillCount",params);
const getNeedCount = (params)=>getAction("/depotHead/getNeedCount",params);
const batchAddDepotHeadAndDetail = (params)=>postAction("/depotHead/batchAddDepotHeadAndDetail",params);
const findStockByDepotAndBarCode = (params)=>getAction("/depotItem/findStockByDepotAndBarCode",params);
const getBatchNumberList = (params)=>getAction("/depotItem/getBatchNumberList",params);
const findFinancialDetailByNumber = (params)=>getAction("/accountHead/getDetailByNumber",params);

const cashierOpenSession = (params)=>postAction("/cashier/session/open",params);
const cashierCloseSession = (params)=>postAction("/cashier/session/close",params);
const cashierCurrentSessionBySeat = (params)=>getAction("/cashier/session/currentBySeat",params);
const cashierBindMember = (params)=>putAction("/cashier/session/bindMember",params);
const cashierSessionUpdate = (params)=>putAction("/cashier/session/update",params);
const cashierSessionDetail = (params)=>getAction("/cashier/session/detail",params);

const cashierServiceItemList = (params)=>getAction("/cashier/serviceItem/list",params);
const cashierAddServiceItem = (params)=>postAction("/cashier/serviceItem/add",params);
const cashierUpdateServiceItem = (params)=>putAction("/cashier/serviceItem/update",params);
const cashierDeleteServiceItem = (params)=>deleteAction("/cashier/serviceItem/delete",params);

const cashierServiceOrderCreate = (params)=>postAction("/cashier/serviceOrder/create",params);
const cashierServiceOrderAddItem = (params)=>postAction("/cashier/serviceOrder/addItem",params);
const cashierServiceOrderQuickAddItem = (params)=>postAction("/cashier/serviceOrder/quickAddItem",params);
const cashierServiceOrderListBySession = (params)=>getAction("/cashier/serviceOrder/listBySession",params);
const cashierServiceOrderListItems = (params)=>getAction("/cashier/serviceOrder/listItems",params);
const cashierServiceOrderFinish = (params)=>postAction("/cashier/serviceOrder/finish",params);
const cashierServiceOrderItemUpdateQty = (params)=>putAction("/cashier/serviceOrder/item/updateQty",params);
const cashierServiceOrderItemDelete = (params)=>postAction("/cashier/serviceOrder/item/delete",params);

const cashierCreditListOpen = (params)=>getAction("/cashier/credit/listOpen",params);
const cashierCreditCreate = (params)=>postAction("/cashier/credit/create",params);
const cashierCreditSettle = (params)=>postAction("/cashier/credit/settle",params);
const cashierCreditCancel = (params)=>postAction("/cashier/credit/cancel",params);

const cashierShiftCurrent = (params)=>getAction("/cashier/shift/current",params);
const cashierShiftOpen = (params)=>postAction("/cashier/shift/open",params);
const cashierShiftHandover = (params)=>postAction("/cashier/shift/handover",params);

const cashierCartProductAdd = (params)=>postAction("/cashier/cart/product/add",params);
const cashierCartProductUpdateQty = (params)=>putAction("/cashier/cart/product/updateQty",params);
const cashierCartProductDelete = (params)=>postAction("/cashier/cart/product/delete",params);

const cashierSettlementPreview = (params)=>getAction("/cashier/settlement/preview",params);
const cashierSettlementCheckout = (params)=>postAction("/cashier/settlement/checkout",params);

const cashierInvoiceRequestList = (params)=>getAction("/cashier/invoiceRequest/list",params);
const cashierInvoiceRequestMarkIssued = (params)=>putAction("/cashier/invoiceRequest/markIssued",params);
const cashierInvoiceRequestReject = (params)=>putAction("/cashier/invoiceRequest/reject",params);

export {
  getBuyAndSaleStatistics,
  buyOrSalePrice,
  checkTenant,
  addTenant,
  editTenant,
  addRole,
  editRole,
  checkRole,
  roleAllList,
  getTenantRoleList,
  registerUser,
  addUser,
  editUser,
  getUserList,
  getUserBtnByCurrentUser,
  queryPermissionsByUser,
  queryOrganizationTreeList,
  getAllOrganizationTreeByUser,
  queryOrganizationById,
  checkOrganization,
  addPerson,
  editPerson,
  checkPerson,
  getPersonByType,
  getPersonByNumType,
  addAccount,
  editAccount,
  checkAccount,
  getAccount,
  addInOutItem,
  editInOutItem,
  checkInOutItem,
  findInOutItemByParam,
  addDepot,
  editDepot,
  checkDepot,
  addOrUpdateMaterialProperty,
  queryMaterialCategoryTreeList,
  queryMaterialCategoryById,
  checkMaterialCategory,
  addMaterial,
  editMaterial,
  checkMaterial,
  getMaterialBySelect,
  getSerialMaterialBySelect,
  getMaterialByParam,
  getMaterialByBarCode,
  getMaxBarCode,
  checkMaterialBarCode,
  batchUpdateMaterial,
  changeNameToPinYin,
  batAddSerialNumber,
  getEnableSerialNumberList,
  addMaterialAttribute,
  editMaterialAttribute,
  checkMaterialAttribute,
  getMaterialAttributeNameList,
  getMaterialAttributeValueListById,
  addFunction,
  editFunction,
  checkFunction,
  checkNumber,
  addSystemConfig,
  editSystemConfig,
  checkSystemConfig,
  getCurrentSystemConfig,
  fileSizeLimit,
  addPlatformConfig,
  editPlatformConfig,
  getPlatformConfigByKey,
  addUserBusiness,
  editUserBusiness,
  checkUserBusiness,
  updateBtnStrByRoleId,
  updateOneValueByKeyIdAndType,
  addUnit,
  editUnit,
  checkUnit,
  addSupplier,
  editSupplier,
  checkSupplier,
  findBySelectSup,
  findBySelectCus,
  findBySelectRetail,
  findBySelectOrgan,
  findBillDetailByNumber,
  waitBillCount,
  getNeedCount,
  batchAddDepotHeadAndDetail,
  findStockByDepotAndBarCode,
  getBatchNumberList,
  findFinancialDetailByNumber,
  cashierOpenSession,
  cashierCloseSession,
  cashierCurrentSessionBySeat,
  cashierBindMember,
  cashierSessionUpdate,
  cashierSessionDetail,
  cashierServiceItemList,
  cashierAddServiceItem,
  cashierUpdateServiceItem,
  cashierDeleteServiceItem,
  cashierServiceOrderCreate,
  cashierServiceOrderAddItem,
  cashierServiceOrderQuickAddItem,
  cashierServiceOrderListBySession,
  cashierServiceOrderListItems,
  cashierServiceOrderFinish,
  cashierServiceOrderItemUpdateQty,
  cashierServiceOrderItemDelete,
  cashierCreditListOpen,
  cashierCreditCreate,
  cashierCreditSettle,
  cashierCreditCancel,
  cashierShiftCurrent,
  cashierShiftOpen,
  cashierShiftHandover,
  cashierCartProductAdd,
  cashierCartProductUpdateQty,
  cashierCartProductDelete,
  cashierSettlementPreview,
  cashierSettlementCheckout,
  cashierInvoiceRequestList,
  cashierInvoiceRequestMarkIssued,
  cashierInvoiceRequestReject
}




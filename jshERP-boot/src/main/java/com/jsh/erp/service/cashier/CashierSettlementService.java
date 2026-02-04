package com.jsh.erp.service.cashier;

import com.alibaba.fastjson.JSON;
import com.alibaba.fastjson.JSONArray;
import com.alibaba.fastjson.JSONObject;
import com.jsh.erp.datasource.entities.CashierSettlement;
import com.jsh.erp.datasource.entities.CashierSettlementPayment;
import com.jsh.erp.datasource.entities.CashierSessionProductItem;
import com.jsh.erp.datasource.entities.InvoiceRequest;
import com.jsh.erp.datasource.entities.CashierSession;
import com.jsh.erp.datasource.entities.Supplier;
import com.jsh.erp.datasource.entities.ServiceOrder;
import com.jsh.erp.datasource.entities.ServiceOrderItem;
import com.jsh.erp.datasource.mappers.CashierSettlementMapper;
import com.jsh.erp.datasource.mappers.CashierSettlementPaymentMapper;
import com.jsh.erp.datasource.mappers.InvoiceRequestMapper;
import com.jsh.erp.service.SupplierService;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

import javax.annotation.Resource;
import javax.servlet.http.HttpServletRequest;
import java.math.BigDecimal;
import java.math.RoundingMode;
import java.text.SimpleDateFormat;
import java.util.Date;
import java.util.HashMap;
import java.util.List;
import java.util.Map;

@Service
public class CashierSettlementService {
    @Resource
    private CashierSessionService cashierSessionService;

    @Resource
    private ServiceOrderService serviceOrderService;

    @Resource
    private CashierCartService cashierCartService;

    @Resource
    private CashierSettlementMapper cashierSettlementMapper;

    @Resource
    private CashierSettlementPaymentMapper cashierSettlementPaymentMapper;

    @Resource
    private InvoiceRequestMapper invoiceRequestMapper;

    @Resource
    private SupplierService supplierService;

    public Map<String, Object> preview(Long sessionId, Long tenantId) throws Exception {
        Map<String, Object> detail = cashierSessionService.getDetail(sessionId, tenantId);
        Map<String, Object> result = new HashMap<>();
        BigDecimal serviceTotal = sumServiceAmount(sessionId, tenantId);
        BigDecimal productTotal = sumProductAmount(sessionId, tenantId);
        result.put("sessionId", sessionId);
        result.put("serviceTotalAmount", serviceTotal);
        result.put("productTotalAmount", productTotal);
        result.put("totalAmount", serviceTotal.add(productTotal));
        if (detail != null) {
            result.put("items", detail.get("items"));
            result.put("session", detail.get("session"));
            result.put("seat", detail.get("seat"));
            result.put("member", detail.get("member"));
        }
        return result;
    }

    @Transactional(value = "transactionManager", rollbackFor = Exception.class)
    public Map<String, Object> checkout(JSONObject obj, Long tenantId, Long cashierUserId, HttpServletRequest request) throws Exception {
        Long sessionId = obj.getLong("sessionId");
        Boolean clearSeat = obj.getBoolean("clearSeat");
        if (sessionId == null) {
            throw new RuntimeException("参数错误");
        }

        CashierSession session = cashierSessionService.ensureSessionPermission(sessionId, tenantId);
        if (session.getStatus() != null && !"OPEN".equals(session.getStatus())) {
            throw new RuntimeException("会话已关闭");
        }

        CashierSettlement existed = cashierSettlementMapper.selectLatestBySessionId(sessionId, tenantId);
        if (existed != null && existed.getId() != null && existed.getDeleteFlag() != null && !"1".equals(existed.getDeleteFlag())) {
            throw new RuntimeException("该会话已结算，请勿重复操作");
        }

        Map<String, Object> preview = preview(sessionId, tenantId);
        BigDecimal serviceTotal = getBigDecimal(preview.get("serviceTotalAmount"));
        BigDecimal productTotal = getBigDecimal(preview.get("productTotalAmount"));
        BigDecimal totalAmount = getBigDecimal(preview.get("totalAmount"));

        JSONArray payments = obj.getJSONArray("payments");
        if (payments == null || payments.isEmpty()) {
            throw new RuntimeException("请填写付款方式");
        }

        BigDecimal realPayAmount = BigDecimal.ZERO;
        BigDecimal prepaidUsedAmount = BigDecimal.ZERO;
        for (Object p : payments) {
            JSONObject row = JSONObject.parseObject(JSON.toJSONString(p));
            String method = row.getString("payMethod");
            BigDecimal amount = row.getBigDecimal("amount");
            if (amount == null) {
                continue;
            }
            if (amount.compareTo(BigDecimal.ZERO) < 0) {
                throw new RuntimeException("付款金额不能为负数");
            }
            realPayAmount = realPayAmount.add(amount);
            if ("CARD".equalsIgnoreCase(method)) {
                prepaidUsedAmount = prepaidUsedAmount.add(amount);
            }
        }
        realPayAmount = realPayAmount.setScale(2, RoundingMode.HALF_UP);
        prepaidUsedAmount = prepaidUsedAmount.setScale(2, RoundingMode.HALF_UP);
        BigDecimal totalAmountScale2 = totalAmount.setScale(2, RoundingMode.HALF_UP);

        if (realPayAmount.compareTo(totalAmountScale2) < 0) {
            throw new RuntimeException("实收金额不足");
        }
        BigDecimal changeAmount = realPayAmount.subtract(totalAmountScale2).setScale(2, RoundingMode.HALF_UP);

        BigDecimal balanceBefore = null;
        BigDecimal balanceAfter = null;
        if (prepaidUsedAmount.compareTo(BigDecimal.ZERO) > 0) {
            if (session.getMemberId() == null) {
                throw new RuntimeException("储值支付必须选择会员");
            }
            Object memberObj = preview.get("member");
            Supplier member = memberObj instanceof Supplier ? (Supplier) memberObj : null;
            if (member == null) {
                member = supplierService.getSupplier(session.getMemberId());
            }
            balanceBefore = member == null || member.getAdvanceIn() == null ? BigDecimal.ZERO : member.getAdvanceIn().setScale(2, RoundingMode.HALF_UP);
            if (balanceBefore.compareTo(prepaidUsedAmount) < 0) {
                throw new RuntimeException("会员余额不足");
            }
            balanceAfter = balanceBefore.subtract(prepaidUsedAmount).setScale(2, RoundingMode.HALF_UP);
        }

        CashierSettlement settlement = new CashierSettlement();
        settlement.setSettlementNo(generateSettlementNo(tenantId, sessionId));
        settlement.setSessionId(sessionId);
        settlement.setDepotId(session.getDepotId());
        settlement.setSeatId(session.getSeatId());
        settlement.setMemberId(session.getMemberId());
        settlement.setServiceTotalAmount(serviceTotal);
        settlement.setProductTotalAmount(productTotal);
        settlement.setTotalAmount(totalAmountScale2);
        settlement.setRealPayAmount(realPayAmount);
        settlement.setChangeAmount(changeAmount);
        settlement.setDiscountAmount(BigDecimal.ZERO);
        settlement.setItemsJson(JSON.toJSONString(preview.get("items")));
        settlement.setStatus("PAID");
        settlement.setCashierUserId(cashierUserId);
        settlement.setCreatedTime(new Date());
        settlement.setRemark(obj.getString("remark"));
        settlement.setTenantId(tenantId);
        settlement.setDeleteFlag("0");
        cashierSettlementMapper.insertSelective(settlement);

        cashierSettlementPaymentMapper.deleteBySettlementId(settlement.getId(), tenantId);
        for (Object p : payments) {
            JSONObject row = JSONObject.parseObject(JSON.toJSONString(p));
            String method = row.getString("payMethod");
            BigDecimal amount = row.getBigDecimal("amount");
            if (amount == null || amount.compareTo(BigDecimal.ZERO) <= 0) {
                continue;
            }
            CashierSettlementPayment pay = new CashierSettlementPayment();
            pay.setSettlementId(settlement.getId());
            pay.setPayMethod(method);
            pay.setAmount(amount.setScale(2, RoundingMode.HALF_UP));
            pay.setTenantId(tenantId);
            pay.setDeleteFlag("0");
            cashierSettlementPaymentMapper.insertSelective(pay);
        }

        Boolean needInvoice = obj.getBoolean("needInvoice");
        Long invoiceRequestId = null;
        if (needInvoice != null && needInvoice) {
            JSONObject invoiceInfo = obj.getJSONObject("invoiceInfo");
            if (invoiceInfo == null) {
                throw new RuntimeException("请填写开票信息");
            }
            String buyerType = invoiceInfo.getString("buyerType");
            String buyerName = invoiceInfo.getString("buyerName");
            String taxNo = invoiceInfo.getString("taxNo");
            String email = invoiceInfo.getString("email");
            String phone = invoiceInfo.getString("phone");
            String invoiceType = invoiceInfo.getString("invoiceType");
            String invoiceContent = invoiceInfo.getString("invoiceContent");

            if (buyerName == null || buyerName.trim().isEmpty()) {
                throw new RuntimeException("抬头名称不能为空");
            }
            if ("COMPANY".equalsIgnoreCase(buyerType) && (taxNo == null || taxNo.trim().isEmpty())) {
                throw new RuntimeException("企业开票税号不能为空");
            }
            if (email == null || email.trim().isEmpty()) {
                throw new RuntimeException("邮箱不能为空");
            }

            InvoiceRequest req = new InvoiceRequest();
            req.setSettlementId(settlement.getId());
            req.setSessionId(sessionId);
            req.setDepotId(session.getDepotId());
            req.setMemberId(session.getMemberId());
            req.setBuyerType(buyerType);
            req.setBuyerName(buyerName);
            req.setTaxNo(taxNo);
            req.setEmail(email);
            req.setPhone(phone);
            req.setInvoiceType(invoiceType == null || invoiceType.trim().isEmpty() ? "ELECTRONIC_NORMAL" : invoiceType);
            req.setInvoiceContent(invoiceContent == null || invoiceContent.trim().isEmpty() ? "服务费" : invoiceContent);
            req.setAmount(totalAmountScale2);
            req.setStatus("PENDING");
            req.setCreatedTime(new Date());
            req.setRemark(invoiceInfo.getString("remark"));
            req.setTenantId(tenantId);
            req.setDeleteFlag("0");
            invoiceRequestMapper.insertSelective(req);
            invoiceRequestId = req.getId();
        }

        if (prepaidUsedAmount.compareTo(BigDecimal.ZERO) > 0 && session.getMemberId() != null) {
            supplierService.updateAdvanceIn(session.getMemberId());
            Supplier updatedMember = supplierService.getSupplier(session.getMemberId());
            preview.put("member", updatedMember);
            if (updatedMember != null && updatedMember.getAdvanceIn() != null) {
                balanceAfter = updatedMember.getAdvanceIn().setScale(2, RoundingMode.HALF_UP);
            }
        }

        if (clearSeat == null || clearSeat) {
            JSONObject close = new JSONObject();
            close.put("sessionId", sessionId);
            cashierSessionService.closeSession(close, tenantId, request);
        }

        preview.put("settlementId", settlement.getId());
        preview.put("settlementNo", settlement.getSettlementNo());
        preview.put("realPayAmount", realPayAmount);
        preview.put("changeAmount", changeAmount);
        preview.put("invoiceRequestId", invoiceRequestId);
        preview.put("payments", payments);
        preview.put("prepaidUsedAmount", prepaidUsedAmount);
        preview.put("balanceBefore", balanceBefore);
        preview.put("balanceAfter", balanceAfter);
        return preview;
    }

    private BigDecimal sumServiceAmount(Long sessionId, Long tenantId) throws Exception {
        List<ServiceOrder> orders = serviceOrderService.listBySessionId(sessionId, tenantId);
        BigDecimal sum = BigDecimal.ZERO;
        if (orders == null) {
            return sum;
        }
        for (ServiceOrder order : orders) {
            List<ServiceOrderItem> items = serviceOrderService.listItemsByOrderId(order.getId(), tenantId);
            if (items == null) {
                continue;
            }
            for (ServiceOrderItem item : items) {
                if (item != null && item.getAmount() != null) {
                    sum = sum.add(item.getAmount());
                }
            }
        }
        return sum;
    }

    private BigDecimal sumProductAmount(Long sessionId, Long tenantId) throws Exception {
        List<CashierSessionProductItem> items = cashierCartService.listProductsBySessionId(sessionId, tenantId);
        BigDecimal sum = BigDecimal.ZERO;
        if (items == null) {
            return sum;
        }
        for (CashierSessionProductItem item : items) {
            if (item != null && item.getAmount() != null) {
                sum = sum.add(item.getAmount());
            }
        }
        return sum;
    }

    private String generateSettlementNo(Long tenantId, Long sessionId) {
        String date = new SimpleDateFormat("yyyyMMdd").format(new Date());
        String t = tenantId == null ? "0" : tenantId.toString();
        String s = sessionId == null ? "0" : sessionId.toString();
        String suffix = s.length() > 6 ? s.substring(s.length() - 6) : s;
        return "JS" + date + t + suffix;
    }

    private BigDecimal getBigDecimal(Object v) {
        if (v == null) {
            return BigDecimal.ZERO;
        }
        if (v instanceof BigDecimal) {
            return (BigDecimal) v;
        }
        return new BigDecimal(v.toString());
    }
}

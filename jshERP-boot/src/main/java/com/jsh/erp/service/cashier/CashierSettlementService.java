package com.jsh.erp.service.cashier;

import com.alibaba.fastjson.JSONObject;
import com.jsh.erp.datasource.entities.CashierSessionProductItem;
import com.jsh.erp.datasource.entities.ServiceOrder;
import com.jsh.erp.datasource.entities.ServiceOrderItem;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

import javax.annotation.Resource;
import javax.servlet.http.HttpServletRequest;
import java.math.BigDecimal;
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

    public Map<String, Object> preview(Long sessionId, Long tenantId) throws Exception {
        cashierSessionService.ensureSessionPermission(sessionId, tenantId);
        Map<String, Object> result = new HashMap<>();
        BigDecimal serviceTotal = sumServiceAmount(sessionId, tenantId);
        BigDecimal productTotal = sumProductAmount(sessionId, tenantId);
        result.put("sessionId", sessionId);
        result.put("serviceTotalAmount", serviceTotal);
        result.put("productTotalAmount", productTotal);
        result.put("totalAmount", serviceTotal.add(productTotal));
        return result;
    }

    @Transactional(value = "transactionManager", rollbackFor = Exception.class)
    public Map<String, Object> checkout(JSONObject obj, Long tenantId, HttpServletRequest request) throws Exception {
        Long sessionId = obj.getLong("sessionId");
        Boolean clearSeat = obj.getBoolean("clearSeat");
        Map<String, Object> preview = preview(sessionId, tenantId);
        if (clearSeat == null || clearSeat) {
            JSONObject close = new JSONObject();
            close.put("sessionId", sessionId);
            cashierSessionService.closeSession(close, tenantId, request);
        }
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
}

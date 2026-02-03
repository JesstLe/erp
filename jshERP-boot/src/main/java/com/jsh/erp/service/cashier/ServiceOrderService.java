package com.jsh.erp.service.cashier;

import com.alibaba.fastjson.JSONObject;
import com.jsh.erp.datasource.entities.CommissionRule;
import com.jsh.erp.datasource.entities.ServiceItem;
import com.jsh.erp.datasource.entities.ServiceOrder;
import com.jsh.erp.datasource.entities.ServiceOrderItem;
import com.jsh.erp.datasource.mappers.CommissionRuleMapper;
import com.jsh.erp.datasource.mappers.ServiceItemMapper;
import com.jsh.erp.datasource.mappers.ServiceOrderItemMapper;
import com.jsh.erp.datasource.mappers.ServiceOrderMapper;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

import javax.annotation.Resource;
import javax.servlet.http.HttpServletRequest;
import java.math.BigDecimal;
import java.math.RoundingMode;
import java.util.Date;
import java.util.List;

@Service
public class ServiceOrderService {
    @Resource
    private ServiceOrderMapper serviceOrderMapper;

    @Resource
    private ServiceOrderItemMapper serviceOrderItemMapper;

    @Resource
    private ServiceItemMapper serviceItemMapper;

    @Resource
    private CommissionRuleMapper commissionRuleMapper;

    public List<ServiceOrder> listBySessionId(Long sessionId, Long tenantId) throws Exception {
        return serviceOrderMapper.selectBySessionId(sessionId, tenantId);
    }

    public List<ServiceOrderItem> listItemsByOrderId(Long orderId, Long tenantId) throws Exception {
        return serviceOrderItemMapper.selectByOrderId(orderId, tenantId);
    }

    @Transactional(value = "transactionManager", rollbackFor = Exception.class)
    public ServiceOrder createOrder(JSONObject obj, Long tenantId, HttpServletRequest request) throws Exception {
        Long sessionId = obj.getLong("sessionId");
        Long technicianId = obj.getLong("technicianId");
        String remark = obj.getString("remark");

        ServiceOrder order = new ServiceOrder();
        order.setSessionId(sessionId);
        order.setTechnicianId(technicianId);
        order.setStatus("OPEN");
        order.setCreatedTime(new Date());
        order.setTenantId(tenantId);
        order.setRemark(remark);
        order.setDeleteFlag("0");
        serviceOrderMapper.insertSelective(order);
        return order;
    }

    @Transactional(value = "transactionManager", rollbackFor = Exception.class)
    public ServiceOrderItem addItem(JSONObject obj, Long tenantId, HttpServletRequest request) throws Exception {
        Long serviceOrderId = obj.getLong("serviceOrderId");
        Long serviceItemId = obj.getLong("serviceItemId");
        BigDecimal qty = obj.getBigDecimal("qty");
        if (qty == null) {
            qty = BigDecimal.ONE;
        }
        ServiceItem serviceItem = serviceItemMapper.selectByPrimaryKey(serviceItemId);
        BigDecimal unitPrice = serviceItem == null || serviceItem.getPrice() == null ? BigDecimal.ZERO : serviceItem.getPrice();
        BigDecimal amount = unitPrice.multiply(qty).setScale(6, RoundingMode.HALF_UP);

        ServiceOrderItem item = new ServiceOrderItem();
        item.setServiceOrderId(serviceOrderId);
        item.setServiceItemId(serviceItemId);
        item.setQty(qty);
        item.setUnitPrice(unitPrice);
        item.setAmount(amount);
        item.setCommissionAmount(BigDecimal.ZERO);
        item.setTenantId(tenantId);
        item.setDeleteFlag("0");
        serviceOrderItemMapper.insertSelective(item);
        return item;
    }

    @Transactional(value = "transactionManager", rollbackFor = Exception.class)
    public int finishOrder(JSONObject obj, Long tenantId, HttpServletRequest request) throws Exception {
        Long serviceOrderId = obj.getLong("serviceOrderId");
        ServiceOrder order = serviceOrderMapper.selectByPrimaryKey(serviceOrderId);
        if (order == null) {
            return 0;
        }
        List<ServiceOrderItem> items = serviceOrderItemMapper.selectByOrderId(serviceOrderId, tenantId);
        List<CommissionRule> rules = commissionRuleMapper.selectEnabledRules(tenantId);
        for (ServiceOrderItem item : items) {
            BigDecimal commissionAmount = calcCommissionAmount(rules, order.getTechnicianId(), item.getServiceItemId(), item.getQty(), item.getAmount());
            ServiceOrderItem update = new ServiceOrderItem();
            update.setId(item.getId());
            update.setTenantId(tenantId);
            update.setCommissionAmount(commissionAmount);
            serviceOrderItemMapper.updateByPrimaryKeySelective(update);
        }
        ServiceOrder updateOrder = new ServiceOrder();
        updateOrder.setId(serviceOrderId);
        updateOrder.setTenantId(tenantId);
        updateOrder.setStatus("FINISHED");
        updateOrder.setFinishedTime(new Date());
        return serviceOrderMapper.updateByPrimaryKeySelective(updateOrder);
    }

    private BigDecimal calcCommissionAmount(List<CommissionRule> rules, Long technicianId, Long serviceItemId, BigDecimal qty, BigDecimal amount) {
        CommissionRule rule = findRule(rules, technicianId, serviceItemId);
        if (rule == null || rule.getCommissionValue() == null || rule.getCommissionType() == null) {
            return BigDecimal.ZERO;
        }
        if ("PERCENT".equalsIgnoreCase(rule.getCommissionType())) {
            return amount.multiply(rule.getCommissionValue())
                    .divide(new BigDecimal("100"), 6, RoundingMode.HALF_UP);
        }
        if ("FIXED".equalsIgnoreCase(rule.getCommissionType())) {
            BigDecimal q = qty == null ? BigDecimal.ONE : qty;
            return q.multiply(rule.getCommissionValue()).setScale(6, RoundingMode.HALF_UP);
        }
        return BigDecimal.ZERO;
    }

    private CommissionRule findRule(List<CommissionRule> rules, Long technicianId, Long serviceItemId) {
        if (rules == null || rules.isEmpty()) {
            return null;
        }
        for (CommissionRule rule : rules) {
            if (!"TECHNICIAN_ITEM".equalsIgnoreCase(rule.getRuleScope())) {
                continue;
            }
            if (technicianId != null && technicianId.equals(rule.getTechnicianId())
                    && serviceItemId != null && serviceItemId.equals(rule.getServiceItemId())) {
                return rule;
            }
        }
        for (CommissionRule rule : rules) {
            if (!"SERVICE_ITEM".equalsIgnoreCase(rule.getRuleScope())) {
                continue;
            }
            if (serviceItemId != null && serviceItemId.equals(rule.getServiceItemId())) {
                return rule;
            }
        }
        for (CommissionRule rule : rules) {
            if (!"TECHNICIAN_ALL".equalsIgnoreCase(rule.getRuleScope())) {
                continue;
            }
            if (technicianId != null && technicianId.equals(rule.getTechnicianId())) {
                return rule;
            }
        }
        return null;
    }
}


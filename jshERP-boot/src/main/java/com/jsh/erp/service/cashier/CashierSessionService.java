package com.jsh.erp.service.cashier;

import com.alibaba.fastjson.JSONObject;
import com.jsh.erp.datasource.entities.CashierSession;
import com.jsh.erp.datasource.entities.CashierSessionProductItem;
import com.jsh.erp.datasource.entities.Seat;
import com.jsh.erp.datasource.entities.ServiceItem;
import com.jsh.erp.datasource.entities.ServiceOrder;
import com.jsh.erp.datasource.entities.ServiceOrderItem;
import com.jsh.erp.datasource.entities.Supplier;
import com.jsh.erp.datasource.mappers.CashierSessionMapper;
import com.jsh.erp.datasource.mappers.ServiceItemMapper;
import com.jsh.erp.datasource.mappers.SupplierMapper;
import com.jsh.erp.datasource.mappers.SeatMapper;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

import javax.annotation.Resource;
import javax.servlet.http.HttpServletRequest;
import java.math.BigDecimal;
import java.util.Date;
import java.util.HashMap;
import java.util.List;
import java.util.Map;

@Service
public class CashierSessionService {
    @Resource
    private CashierSessionMapper cashierSessionMapper;

    @Resource
    private SeatMapper seatMapper;

    @Resource
    private SupplierMapper supplierMapper;

    @Resource
    private ServiceOrderService serviceOrderService;

    @Resource
    private CashierCartService cashierCartService;

    @Resource
    private ServiceItemMapper serviceItemMapper;

    public CashierSession getCurrentBySeatId(Long seatId, Long tenantId) throws Exception {
        return cashierSessionMapper.selectCurrentBySeatId(seatId, tenantId);
    }

    public CashierSession getById(Long id) throws Exception {
        return cashierSessionMapper.selectByPrimaryKey(id);
    }

    public Map<String, Object> getDetail(Long sessionId, Long tenantId) throws Exception {
        CashierSession session = cashierSessionMapper.selectByPrimaryKey(sessionId);
        if (session == null) {
            return null;
        }
        if (tenantId != null && session.getTenantId() != null && !tenantId.equals(session.getTenantId())) {
            return null;
        }
        Seat seat = session.getSeatId() == null ? null : seatMapper.selectByPrimaryKey(session.getSeatId());
        Supplier member = session.getMemberId() == null ? null : supplierMapper.selectByPrimaryKey(session.getMemberId());

        List<ServiceOrder> orders = serviceOrderService.listBySessionId(sessionId, tenantId);
        List<CashierSessionProductItem> productItems = cashierCartService.listProductsBySessionId(sessionId, tenantId);

        BigDecimal total = BigDecimal.ZERO;
        List<Map<String, Object>> items = new java.util.ArrayList<>();

        if (orders != null) {
            for (ServiceOrder order : orders) {
                List<ServiceOrderItem> orderItems = serviceOrderService.listItemsByOrderId(order.getId(), tenantId);
                if (orderItems == null) {
                    continue;
                }
                for (ServiceOrderItem it : orderItems) {
                    ServiceItem serviceItem = it.getServiceItemId() == null ? null : serviceItemMapper.selectByPrimaryKey(it.getServiceItemId());
                    Map<String, Object> row = new HashMap<>();
                    row.put("type", "服务");
                    row.put("id", it.getId());
                    row.put("name", serviceItem != null ? serviceItem.getName() : ("项目#" + it.getServiceItemId()));
                    row.put("qty", it.getQty());
                    row.put("unitPrice", it.getUnitPrice());
                    row.put("amount", it.getAmount());
                    row.put("refType", "SERVICE");
                    row.put("refId", it.getServiceItemId());
                    items.add(row);
                    if (it.getAmount() != null) {
                        total = total.add(it.getAmount());
                    }
                }
            }
        }

        if (productItems != null) {
            for (CashierSessionProductItem it : productItems) {
                Map<String, Object> row = new HashMap<>();
                row.put("type", "产品");
                row.put("id", it.getId());
                row.put("name", it.getMaterialNameSnap());
                row.put("qty", it.getQty());
                row.put("unitPrice", it.getUnitPrice());
                row.put("amount", it.getAmount());
                row.put("refType", "PRODUCT");
                row.put("refId", it.getMaterialId());
                items.add(row);
                if (it.getAmount() != null) {
                    total = total.add(it.getAmount());
                }
            }
        }

        Map<String, Object> result = new HashMap<>();
        result.put("session", session);
        result.put("seat", seat);
        result.put("member", member);
        result.put("items", items);
        result.put("totalAmount", total);
        return result;
    }

    @Transactional(value = "transactionManager", rollbackFor = Exception.class)
    public CashierSession openSession(JSONObject obj, Long tenantId, Long operatorUserId, HttpServletRequest request) throws Exception {
        Long seatId = obj.getLong("seatId");
        Long depotId = obj.getLong("depotId");
        Long memberId = obj.getLong("memberId");
        String remark = obj.getString("remark");

        CashierSession existed = cashierSessionMapper.selectCurrentBySeatId(seatId, tenantId);
        if (existed != null) {
            return existed;
        }

        CashierSession session = new CashierSession();
        session.setSeatId(seatId);
        session.setDepotId(depotId);
        session.setMemberId(memberId);
        session.setStatus("OPEN");
        session.setStartTime(new Date());
        session.setOperatorUserId(operatorUserId);
        session.setTenantId(tenantId);
        session.setRemark(remark);
        session.setDeleteFlag("0");
        cashierSessionMapper.insertSelective(session);

        if (seatId != null) {
            Seat seat = new Seat();
            seat.setId(seatId);
            seat.setStatus(1);
            seatMapper.updateByPrimaryKeySelective(seat);
        }

        return session;
    }

    @Transactional(value = "transactionManager", rollbackFor = Exception.class)
    public int bindMember(JSONObject obj, Long tenantId, HttpServletRequest request) throws Exception {
        Long sessionId = obj.getLong("sessionId");
        Long memberId = obj.getLong("memberId");
        CashierSession session = new CashierSession();
        session.setId(sessionId);
        session.setTenantId(tenantId);
        session.setMemberId(memberId);
        return cashierSessionMapper.updateByPrimaryKeySelective(session);
    }

    @Transactional(value = "transactionManager", rollbackFor = Exception.class)
    public int update(JSONObject obj, Long tenantId, HttpServletRequest request) throws Exception {
        Long sessionId = obj.getLong("sessionId");
        String remark = obj.getString("remark");
        CashierSession db = cashierSessionMapper.selectByPrimaryKey(sessionId);
        if (db == null) {
            return 0;
        }
        if (tenantId != null && db.getTenantId() != null && !tenantId.equals(db.getTenantId())) {
            return 0;
        }
        if (db.getStatus() != null && !"OPEN".equals(db.getStatus())) {
            return 0;
        }
        CashierSession session = new CashierSession();
        session.setId(sessionId);
        session.setTenantId(tenantId);
        session.setRemark(remark);
        return cashierSessionMapper.updateByPrimaryKeySelective(session);
    }

    @Transactional(value = "transactionManager", rollbackFor = Exception.class)
    public int closeSession(JSONObject obj, Long tenantId, HttpServletRequest request) throws Exception {
        Long sessionId = obj.getLong("sessionId");
        CashierSession db = cashierSessionMapper.selectByPrimaryKey(sessionId);
        if (db == null) {
            return 0;
        }
        CashierSession session = new CashierSession();
        session.setId(sessionId);
        session.setTenantId(tenantId);
        session.setStatus("CLOSED");
        session.setEndTime(new Date());
        int result = cashierSessionMapper.updateByPrimaryKeySelective(session);
        if (result > 0 && db.getSeatId() != null) {
            Seat seat = new Seat();
            seat.setId(db.getSeatId());
            seat.setStatus(0);
            seatMapper.updateByPrimaryKeySelective(seat);
        }
        return result;
    }
}

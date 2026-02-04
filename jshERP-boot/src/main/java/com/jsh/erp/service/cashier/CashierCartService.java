package com.jsh.erp.service.cashier;

import com.alibaba.fastjson.JSONObject;
import com.jsh.erp.datasource.entities.CashierSession;
import com.jsh.erp.datasource.entities.CashierSessionProductItem;
import com.jsh.erp.datasource.mappers.CashierSessionProductItemMapper;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

import javax.annotation.Resource;
import javax.servlet.http.HttpServletRequest;
import java.math.BigDecimal;
import java.math.RoundingMode;
import java.util.Date;
import java.util.List;

@Service
public class CashierCartService {
    @Resource
    private CashierSessionProductItemMapper cashierSessionProductItemMapper;

    @Resource
    private CashierSessionService cashierSessionService;

    public List<CashierSessionProductItem> listProductsBySessionId(Long sessionId, Long tenantId) throws Exception {
        cashierSessionService.ensureSessionPermission(sessionId, tenantId);
        return cashierSessionProductItemMapper.selectBySessionId(sessionId, tenantId);
    }

    @Transactional(value = "transactionManager", rollbackFor = Exception.class)
    public int updateProductQty(JSONObject obj, Long tenantId, HttpServletRequest request) throws Exception {
        Long id = obj.getLong("id");
        BigDecimal qty = obj.getBigDecimal("qty");
        if (id == null || qty == null || qty.compareTo(BigDecimal.ZERO) <= 0) {
            return 0;
        }
        CashierSessionProductItem db = cashierSessionProductItemMapper.selectByPrimaryKey(id);
        if (db == null) {
            return 0;
        }
        if (tenantId != null && db.getTenantId() != null && !tenantId.equals(db.getTenantId())) {
            return 0;
        }
        cashierSessionService.ensureSessionPermission(db.getSessionId(), tenantId);
        BigDecimal unitPrice = db.getUnitPrice() == null ? BigDecimal.ZERO : db.getUnitPrice();
        BigDecimal amount = unitPrice.multiply(qty).setScale(6, RoundingMode.HALF_UP);
        CashierSessionProductItem update = new CashierSessionProductItem();
        update.setId(id);
        update.setTenantId(tenantId);
        update.setQty(qty);
        update.setAmount(amount);
        return cashierSessionProductItemMapper.updateByPrimaryKeySelective(update);
    }

    @Transactional(value = "transactionManager", rollbackFor = Exception.class)
    public int deleteProduct(JSONObject obj, Long tenantId, HttpServletRequest request) throws Exception {
        Long id = obj.getLong("id");
        if (id == null) {
            return 0;
        }
        CashierSessionProductItem db = cashierSessionProductItemMapper.selectByPrimaryKey(id);
        if (db == null) {
            return 0;
        }
        if (tenantId != null && db.getTenantId() != null && !tenantId.equals(db.getTenantId())) {
            return 0;
        }
        cashierSessionService.ensureSessionPermission(db.getSessionId(), tenantId);
        CashierSessionProductItem update = new CashierSessionProductItem();
        update.setId(id);
        update.setTenantId(tenantId);
        update.setDeleteFlag("1");
        return cashierSessionProductItemMapper.updateByPrimaryKeySelective(update);
    }

    @Transactional(value = "transactionManager", rollbackFor = Exception.class)
    public CashierSessionProductItem addProduct(JSONObject obj, Long tenantId, HttpServletRequest request) throws Exception {
        Long sessionId = obj.getLong("sessionId");
        Long materialId = obj.getLong("materialId");
        String materialName = obj.getString("materialName");
        BigDecimal unitPrice = obj.getBigDecimal("unitPrice");
        BigDecimal qty = obj.getBigDecimal("qty");
        if (qty == null || qty.compareTo(BigDecimal.ZERO) <= 0) {
            qty = BigDecimal.ONE;
        }
        if (unitPrice == null || unitPrice.compareTo(BigDecimal.ZERO) < 0) {
            unitPrice = BigDecimal.ZERO;
        }

        CashierSession session = cashierSessionService.ensureSessionPermission(sessionId, tenantId);
        if (!"OPEN".equalsIgnoreCase(session.getStatus())) {
            throw new IllegalArgumentException("会话已关闭");
        }

        CashierSessionProductItem existed = cashierSessionProductItemMapper.selectBySessionAndMaterial(sessionId, materialId, tenantId);
        if (existed != null) {
            BigDecimal newQty = existed.getQty() == null ? qty : existed.getQty().add(qty);
            BigDecimal newAmount = unitPrice.multiply(newQty).setScale(6, RoundingMode.HALF_UP);
            CashierSessionProductItem update = new CashierSessionProductItem();
            update.setId(existed.getId());
            update.setTenantId(tenantId);
            update.setQty(newQty);
            update.setUnitPrice(unitPrice);
            update.setAmount(newAmount);
            if (materialName != null && materialName.length() > 0) {
                update.setMaterialNameSnap(materialName);
            }
            cashierSessionProductItemMapper.updateByPrimaryKeySelective(update);
            return cashierSessionProductItemMapper.selectByPrimaryKey(existed.getId());
        }

        BigDecimal amount = unitPrice.multiply(qty).setScale(6, RoundingMode.HALF_UP);
        CashierSessionProductItem record = new CashierSessionProductItem();
        record.setSessionId(sessionId);
        record.setMaterialId(materialId);
        record.setMaterialNameSnap(materialName);
        record.setUnitPrice(unitPrice);
        record.setQty(qty);
        record.setAmount(amount);
        record.setCreateTime(new Date());
        record.setTenantId(tenantId);
        record.setDeleteFlag("0");
        cashierSessionProductItemMapper.insertSelective(record);
        return record;
    }
}

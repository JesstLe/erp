package com.jsh.erp.service.cashier;

import com.alibaba.fastjson.JSONObject;
import com.jsh.erp.datasource.entities.CashierShift;
import com.jsh.erp.datasource.mappers.CashierShiftMapper;
import com.jsh.erp.datasource.mappers.ServiceOrderItemMapper;
import com.jsh.erp.service.DepotService;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

import javax.annotation.Resource;
import javax.servlet.http.HttpServletRequest;
import java.math.BigDecimal;
import java.math.RoundingMode;
import java.util.Date;
import java.util.List;
import java.util.Map;

@Service
public class CashierShiftService {
    @Resource
    private CashierShiftMapper cashierShiftMapper;

    @Resource
    private ServiceOrderItemMapper serviceOrderItemMapper;

    @Resource
    private DepotService depotService;

    public CashierShift getOpenShift(Long depotId, Long cashierUserId, Long tenantId) throws Exception {
        depotService.ensureCurrentUserDepotPermission(depotId);
        return cashierShiftMapper.selectOpenShift(depotId, cashierUserId, tenantId);
    }

    @Transactional(value = "transactionManager", rollbackFor = Exception.class)
    public CashierShift openShift(JSONObject obj, Long tenantId, Long cashierUserId, HttpServletRequest request) throws Exception {
        Long depotId = obj.getLong("depotId");
        depotService.ensureCurrentUserDepotPermission(depotId);
        CashierShift existed = cashierShiftMapper.selectOpenShift(depotId, cashierUserId, tenantId);
        if (existed != null) {
            return existed;
        }
        CashierShift shift = new CashierShift();
        shift.setDepotId(depotId);
        shift.setCashierUserId(cashierUserId);
        shift.setStartTime(new Date());
        shift.setOpeningAmount(obj.getBigDecimal("openingAmount"));
        shift.setClosingAmount(BigDecimal.ZERO);
        shift.setTotalAmount(BigDecimal.ZERO);
        shift.setDiffAmount(BigDecimal.ZERO);
        shift.setRemark(obj.getString("remark"));
        shift.setTenantId(tenantId);
        shift.setDeleteFlag("0");
        cashierShiftMapper.insertSelective(shift);
        return shift;
    }

    @Transactional(value = "transactionManager", rollbackFor = Exception.class)
    public int handover(JSONObject obj, Long tenantId, Long cashierUserId, HttpServletRequest request) throws Exception {
        Long shiftId = obj.getLong("shiftId");
        CashierShift shift = cashierShiftMapper.selectByPrimaryKey(shiftId);
        if (shift == null) {
            return 0;
        }
        if (tenantId != null && shift.getTenantId() != null && !tenantId.equals(shift.getTenantId())) {
            return 0;
        }
        if (cashierUserId != null && shift.getCashierUserId() != null && !cashierUserId.equals(shift.getCashierUserId())) {
            return 0;
        }
        depotService.ensureCurrentUserDepotPermission(shift.getDepotId());
        Date endTime = new Date();
        BigDecimal closingAmount = obj.getBigDecimal("closingAmount");
        if (closingAmount == null) {
            closingAmount = BigDecimal.ZERO;
        }

        BigDecimal totalAmount = BigDecimal.ZERO;
        List<Map<String, Object>> sumList = serviceOrderItemMapper.sumAmountByDepotAndTime(shift.getDepotId(), shift.getStartTime(), endTime, tenantId);
        if (sumList != null && !sumList.isEmpty() && sumList.get(0) != null && sumList.get(0).get("totalAmount") != null) {
            Object v = sumList.get(0).get("totalAmount");
            if (v instanceof BigDecimal) {
                totalAmount = (BigDecimal) v;
            } else {
                totalAmount = new BigDecimal(v.toString());
            }
        }
        totalAmount = totalAmount.setScale(6, RoundingMode.HALF_UP);
        BigDecimal diffAmount = closingAmount.subtract(totalAmount).setScale(6, RoundingMode.HALF_UP);

        CashierShift update = new CashierShift();
        update.setId(shiftId);
        update.setTenantId(tenantId);
        update.setEndTime(endTime);
        update.setClosingAmount(closingAmount);
        update.setTotalAmount(totalAmount);
        update.setDiffAmount(diffAmount);
        update.setRemark(obj.getString("remark"));
        return cashierShiftMapper.updateByPrimaryKeySelective(update);
    }
}

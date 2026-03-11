package com.jsh.erp.service.cashier;

import com.alibaba.fastjson.JSONObject;
import com.jsh.erp.datasource.entities.CashierSession;
import com.jsh.erp.datasource.entities.CreditBill;
import com.jsh.erp.datasource.mappers.CashierSessionMapper;
import com.jsh.erp.datasource.mappers.CreditBillMapper;
import com.jsh.erp.service.DepotService;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

import javax.annotation.Resource;
import javax.servlet.http.HttpServletRequest;
import java.util.Date;
import java.util.List;

@Service
public class CreditBillService {
    @Resource
    private CreditBillMapper creditBillMapper;

    @Resource
    private CashierSessionMapper cashierSessionMapper;

    @Resource
    private DepotService depotService;

    public List<CreditBill> listByStatus(String status, Long tenantId) throws Exception {
        if(tenantId == null) {
            return creditBillMapper.selectByStatus(status, tenantId);
        }
        List<Long> depotIdList = new java.util.ArrayList<>(depotService.getCurrentUserDepotIdSet());
        if(depotIdList.isEmpty()) {
            return new java.util.ArrayList<>();
        }
        return creditBillMapper.selectByStatusAndDepotIds(status, depotIdList, tenantId);
    }

    @Transactional(value = "transactionManager", rollbackFor = Exception.class)
    public int create(JSONObject obj, Long tenantId, HttpServletRequest request) throws Exception {
        CreditBill bill = JSONObject.parseObject(obj.toJSONString(), CreditBill.class);
        CashierSession session = bill.getSessionId() == null ? null : cashierSessionMapper.selectByPrimaryKey(bill.getSessionId());
        if(session == null) {
            throw new RuntimeException("会话不存在");
        }
        if(tenantId != null && session.getTenantId() != null && !tenantId.equals(session.getTenantId())) {
            throw new RuntimeException("无权限");
        }
        depotService.ensureCurrentUserDepotPermission(session.getDepotId());
        bill.setTenantId(tenantId);
        bill.setStatus("OPEN");
        bill.setCreatedTime(new Date());
        bill.setDeleteFlag("0");
        return creditBillMapper.insertSelective(bill);
    }

    @Transactional(value = "transactionManager", rollbackFor = Exception.class)
    public int settle(JSONObject obj, Long tenantId, HttpServletRequest request) throws Exception {
        Long id = obj.getLong("id");
        CreditBill db = creditBillMapper.selectByPrimaryKey(id);
        if(db == null) {
            return 0;
        }
        if(tenantId != null && db.getTenantId() != null && !tenantId.equals(db.getTenantId())) {
            return 0;
        }
        CashierSession session = db.getSessionId() == null ? null : cashierSessionMapper.selectByPrimaryKey(db.getSessionId());
        if(session == null) {
            return 0;
        }
        depotService.ensureCurrentUserDepotPermission(session.getDepotId());
        CreditBill bill = new CreditBill();
        bill.setId(id);
        bill.setTenantId(tenantId);
        bill.setStatus("SETTLED");
        bill.setSettledTime(new Date());
        return creditBillMapper.updateByPrimaryKeySelective(bill);
    }

    @Transactional(value = "transactionManager", rollbackFor = Exception.class)
    public int cancel(JSONObject obj, Long tenantId, HttpServletRequest request) throws Exception {
        Long id = obj.getLong("id");
        CreditBill db = creditBillMapper.selectByPrimaryKey(id);
        if(db == null) {
            return 0;
        }
        if(tenantId != null && db.getTenantId() != null && !tenantId.equals(db.getTenantId())) {
            return 0;
        }
        CashierSession session = db.getSessionId() == null ? null : cashierSessionMapper.selectByPrimaryKey(db.getSessionId());
        if(session == null) {
            return 0;
        }
        depotService.ensureCurrentUserDepotPermission(session.getDepotId());
        CreditBill bill = new CreditBill();
        bill.setId(id);
        bill.setTenantId(tenantId);
        bill.setStatus("CANCELED");
        return creditBillMapper.updateByPrimaryKeySelective(bill);
    }
}

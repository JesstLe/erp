package com.jsh.erp.service.cashier;

import com.alibaba.fastjson.JSONObject;
import com.jsh.erp.datasource.entities.CreditBill;
import com.jsh.erp.datasource.mappers.CreditBillMapper;
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

    public List<CreditBill> listByStatus(String status, Long tenantId) throws Exception {
        return creditBillMapper.selectByStatus(status, tenantId);
    }

    @Transactional(value = "transactionManager", rollbackFor = Exception.class)
    public int create(JSONObject obj, Long tenantId, HttpServletRequest request) throws Exception {
        CreditBill bill = JSONObject.parseObject(obj.toJSONString(), CreditBill.class);
        bill.setTenantId(tenantId);
        bill.setStatus("OPEN");
        bill.setCreatedTime(new Date());
        bill.setDeleteFlag("0");
        return creditBillMapper.insertSelective(bill);
    }

    @Transactional(value = "transactionManager", rollbackFor = Exception.class)
    public int settle(JSONObject obj, Long tenantId, HttpServletRequest request) throws Exception {
        Long id = obj.getLong("id");
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
        CreditBill bill = new CreditBill();
        bill.setId(id);
        bill.setTenantId(tenantId);
        bill.setStatus("CANCELED");
        return creditBillMapper.updateByPrimaryKeySelective(bill);
    }
}


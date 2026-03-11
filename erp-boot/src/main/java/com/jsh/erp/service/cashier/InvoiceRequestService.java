package com.jsh.erp.service.cashier;

import com.alibaba.fastjson.JSONObject;
import com.jsh.erp.datasource.entities.InvoiceRequest;
import com.jsh.erp.datasource.mappers.InvoiceRequestMapper;
import com.jsh.erp.service.DepotService;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

import javax.annotation.Resource;
import javax.servlet.http.HttpServletRequest;
import java.util.Date;
import java.util.HashMap;
import java.util.List;
import java.util.Map;

@Service
public class InvoiceRequestService {
    @Resource
    private InvoiceRequestMapper invoiceRequestMapper;

    @Resource
    private DepotService depotService;

    public Map<String, Object> list(Long depotId, String status, String keyword, Integer currentPage, Integer pageSize, Long tenantId) throws Exception {
        if (depotId != null) {
            depotService.ensureCurrentUserDepotPermission(depotId);
        }
        int page = currentPage == null || currentPage <= 0 ? 1 : currentPage;
        int size = pageSize == null || pageSize <= 0 ? 20 : pageSize;
        int offset = (page - 1) * size;
        List<InvoiceRequest> rows = invoiceRequestMapper.selectList(depotId, status, keyword, tenantId, offset, size);
        Long total = invoiceRequestMapper.countList(depotId, status, keyword, tenantId);
        Map<String, Object> data = new HashMap<>();
        data.put("rows", rows);
        data.put("total", total == null ? 0L : total);
        return data;
    }

    @Transactional(value = "transactionManager", rollbackFor = Exception.class)
    public int markIssued(JSONObject obj, Long tenantId, Long processorUserId, HttpServletRequest request) throws Exception {
        Long id = obj.getLong("id");
        if (id == null) {
            return 0;
        }
        InvoiceRequest db = invoiceRequestMapper.selectByPrimaryKey(id);
        if (db == null) {
            return 0;
        }
        if (tenantId != null && db.getTenantId() != null && !tenantId.equals(db.getTenantId())) {
            return 0;
        }
        depotService.ensureCurrentUserDepotPermission(db.getDepotId());

        InvoiceRequest update = new InvoiceRequest();
        update.setId(id);
        update.setTenantId(tenantId);
        update.setStatus("ISSUED");
        update.setInvoiceNo(obj.getString("invoiceNo"));
        update.setFileUrl(obj.getString("fileUrl"));
        update.setProcessedTime(new Date());
        update.setProcessorUserId(processorUserId);
        update.setRemark(obj.getString("remark"));
        return invoiceRequestMapper.updateByPrimaryKeySelective(update);
    }

    @Transactional(value = "transactionManager", rollbackFor = Exception.class)
    public int reject(JSONObject obj, Long tenantId, Long processorUserId, HttpServletRequest request) throws Exception {
        Long id = obj.getLong("id");
        if (id == null) {
            return 0;
        }
        InvoiceRequest db = invoiceRequestMapper.selectByPrimaryKey(id);
        if (db == null) {
            return 0;
        }
        if (tenantId != null && db.getTenantId() != null && !tenantId.equals(db.getTenantId())) {
            return 0;
        }
        depotService.ensureCurrentUserDepotPermission(db.getDepotId());

        InvoiceRequest update = new InvoiceRequest();
        update.setId(id);
        update.setTenantId(tenantId);
        update.setStatus("REJECTED");
        update.setProcessedTime(new Date());
        update.setProcessorUserId(processorUserId);
        update.setRemark(obj.getString("remark"));
        return invoiceRequestMapper.updateByPrimaryKeySelective(update);
    }
}


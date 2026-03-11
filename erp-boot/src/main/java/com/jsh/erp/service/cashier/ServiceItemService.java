package com.jsh.erp.service.cashier;

import com.alibaba.fastjson.JSONObject;
import com.jsh.erp.datasource.entities.ServiceItem;
import com.jsh.erp.datasource.mappers.ServiceItemMapper;
import com.jsh.erp.utils.StringUtil;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

import javax.annotation.Resource;
import javax.servlet.http.HttpServletRequest;
import java.util.List;

@Service
public class ServiceItemService {
    @Resource
    private ServiceItemMapper serviceItemMapper;

    public List<ServiceItem> select(String name, Boolean enabled, Long tenantId, int offset, int rows) throws Exception {
        return serviceItemMapper.selectByCondition(name, enabled, tenantId, offset, rows);
    }

    public Long count(String name, Boolean enabled, Long tenantId) throws Exception {
        return serviceItemMapper.countsByCondition(name, enabled, tenantId);
    }

    public ServiceItem getServiceItem(Long id) throws Exception {
        return serviceItemMapper.selectByPrimaryKey(id);
    }

    @Transactional(value = "transactionManager", rollbackFor = Exception.class)
    public int insert(JSONObject obj, Long tenantId, HttpServletRequest request) throws Exception {
        ServiceItem serviceItem = JSONObject.parseObject(obj.toJSONString(), ServiceItem.class);
        if (serviceItem.getEnabled() == null) {
            serviceItem.setEnabled(true);
        }
        serviceItem.setTenantId(tenantId);
        serviceItem.setDeleteFlag("0");
        return serviceItemMapper.insertSelective(serviceItem);
    }

    @Transactional(value = "transactionManager", rollbackFor = Exception.class)
    public int update(JSONObject obj, Long tenantId, HttpServletRequest request) throws Exception {
        ServiceItem serviceItem = JSONObject.parseObject(obj.toJSONString(), ServiceItem.class);
        serviceItem.setTenantId(tenantId);
        return serviceItemMapper.updateByPrimaryKeySelective(serviceItem);
    }

    @Transactional(value = "transactionManager", rollbackFor = Exception.class)
    public int delete(Long id, Long tenantId, HttpServletRequest request) throws Exception {
        ServiceItem serviceItem = new ServiceItem();
        serviceItem.setId(id);
        serviceItem.setTenantId(tenantId);
        serviceItem.setDeleteFlag("1");
        return serviceItemMapper.updateByPrimaryKeySelective(serviceItem);
    }

    @Transactional(value = "transactionManager", rollbackFor = Exception.class)
    public int deleteBatch(String ids, Long tenantId, HttpServletRequest request) throws Exception {
        List<Long> idList = StringUtil.strToLongList(ids);
        int result = 0;
        for (Long id : idList) {
            result += delete(id, tenantId, request);
        }
        return result;
    }
}


package com.jsh.erp.service.cashier;

import com.alibaba.fastjson.JSONObject;
import com.jsh.erp.datasource.entities.CommissionRule;
import com.jsh.erp.datasource.mappers.CommissionRuleMapper;
import com.jsh.erp.utils.StringUtil;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

import javax.annotation.Resource;
import javax.servlet.http.HttpServletRequest;
import java.util.List;

@Service
public class CommissionRuleService {
    @Resource
    private CommissionRuleMapper commissionRuleMapper;

    public List<CommissionRule> listEnabled(Long tenantId) throws Exception {
        return commissionRuleMapper.selectEnabledRules(tenantId);
    }

    @Transactional(value = "transactionManager", rollbackFor = Exception.class)
    public int insert(JSONObject obj, Long tenantId, HttpServletRequest request) throws Exception {
        CommissionRule rule = JSONObject.parseObject(obj.toJSONString(), CommissionRule.class);
        rule.setTenantId(tenantId);
        if (rule.getEnabled() == null) {
            rule.setEnabled(true);
        }
        rule.setDeleteFlag("0");
        return commissionRuleMapper.insertSelective(rule);
    }

    @Transactional(value = "transactionManager", rollbackFor = Exception.class)
    public int update(JSONObject obj, Long tenantId, HttpServletRequest request) throws Exception {
        CommissionRule rule = JSONObject.parseObject(obj.toJSONString(), CommissionRule.class);
        rule.setTenantId(tenantId);
        return commissionRuleMapper.updateByPrimaryKeySelective(rule);
    }

    @Transactional(value = "transactionManager", rollbackFor = Exception.class)
    public int delete(Long id, Long tenantId, HttpServletRequest request) throws Exception {
        CommissionRule rule = new CommissionRule();
        rule.setId(id);
        rule.setTenantId(tenantId);
        rule.setDeleteFlag("1");
        return commissionRuleMapper.updateByPrimaryKeySelective(rule);
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


package com.jsh.erp.datasource.mappers;

import com.jsh.erp.datasource.entities.CommissionRule;
import org.apache.ibatis.annotations.Param;

import java.util.List;

public interface CommissionRuleMapper {
    int deleteByPrimaryKey(Long id);

    int insert(CommissionRule record);

    int insertSelective(CommissionRule record);

    CommissionRule selectByPrimaryKey(Long id);

    int updateByPrimaryKeySelective(CommissionRule record);

    int updateByPrimaryKey(CommissionRule record);

    List<CommissionRule> selectEnabledRules(@Param("tenantId") Long tenantId);
}


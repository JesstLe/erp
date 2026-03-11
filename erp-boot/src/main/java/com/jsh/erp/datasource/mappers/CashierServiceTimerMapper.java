package com.jsh.erp.datasource.mappers;

import com.jsh.erp.datasource.entities.CashierServiceTimer;
import org.apache.ibatis.annotations.Param;

public interface CashierServiceTimerMapper {
    int deleteByPrimaryKey(Long id);

    int insert(CashierServiceTimer record);

    int insertSelective(CashierServiceTimer record);

    CashierServiceTimer selectByPrimaryKey(Long id);

    int updateByPrimaryKeySelective(CashierServiceTimer record);

    int updateByPrimaryKey(CashierServiceTimer record);

    CashierServiceTimer selectLatestBySessionId(@Param("sessionId") Long sessionId, @Param("tenantId") Long tenantId);
}


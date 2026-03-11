package com.jsh.erp.datasource.mappers;

import com.jsh.erp.datasource.entities.CashierShift;
import org.apache.ibatis.annotations.Param;

public interface CashierShiftMapper {
    int deleteByPrimaryKey(Long id);

    int insert(CashierShift record);

    int insertSelective(CashierShift record);

    CashierShift selectByPrimaryKey(Long id);

    int updateByPrimaryKeySelective(CashierShift record);

    int updateByPrimaryKey(CashierShift record);

    CashierShift selectOpenShift(@Param("depotId") Long depotId,
                                 @Param("cashierUserId") Long cashierUserId,
                                 @Param("tenantId") Long tenantId);
}


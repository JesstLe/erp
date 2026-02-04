package com.jsh.erp.datasource.mappers;

import com.jsh.erp.datasource.entities.CashierSettlement;
import org.apache.ibatis.annotations.Param;

import java.util.List;

public interface CashierSettlementMapper {
    int deleteByPrimaryKey(Long id);

    int insert(CashierSettlement record);

    int insertSelective(CashierSettlement record);

    CashierSettlement selectByPrimaryKey(Long id);

    int updateByPrimaryKeySelective(CashierSettlement record);

    int updateByPrimaryKey(CashierSettlement record);

    CashierSettlement selectLatestBySessionId(@Param("sessionId") Long sessionId, @Param("tenantId") Long tenantId);

    List<CashierSettlement> selectListByDepotAndTime(@Param("depotId") Long depotId,
                                                     @Param("startTime") String startTime,
                                                     @Param("endTime") String endTime,
                                                     @Param("tenantId") Long tenantId,
                                                     @Param("offset") Integer offset,
                                                     @Param("rows") Integer rows);
}


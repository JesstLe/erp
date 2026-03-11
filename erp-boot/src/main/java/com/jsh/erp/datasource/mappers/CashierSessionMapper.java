package com.jsh.erp.datasource.mappers;

import com.jsh.erp.datasource.entities.CashierSession;
import org.apache.ibatis.annotations.Param;

import java.util.Date;
import java.util.List;

public interface CashierSessionMapper {
    int deleteByPrimaryKey(Long id);

    int insert(CashierSession record);

    int insertSelective(CashierSession record);

    CashierSession selectByPrimaryKey(Long id);

    int updateByPrimaryKeySelective(CashierSession record);

    int updateByPrimaryKey(CashierSession record);

    CashierSession selectCurrentBySeatId(@Param("seatId") Long seatId,
                                         @Param("tenantId") Long tenantId);

    List<CashierSession> selectByDepotAndTime(@Param("depotId") Long depotId,
                                              @Param("startTime") Date startTime,
                                              @Param("endTime") Date endTime,
                                              @Param("tenantId") Long tenantId);
}


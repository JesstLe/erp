package com.jsh.erp.datasource.mappers;

import com.jsh.erp.datasource.entities.CreditBill;
import org.apache.ibatis.annotations.Param;

import java.util.Date;
import java.util.List;

public interface CreditBillMapper {
    int deleteByPrimaryKey(Long id);

    int insert(CreditBill record);

    int insertSelective(CreditBill record);

    CreditBill selectByPrimaryKey(Long id);

    int updateByPrimaryKeySelective(CreditBill record);

    int updateByPrimaryKey(CreditBill record);

    List<CreditBill> selectByStatus(@Param("status") String status,
                                    @Param("tenantId") Long tenantId);

    List<CreditBill> selectByDepotAndTime(@Param("depotId") Long depotId,
                                          @Param("startTime") Date startTime,
                                          @Param("endTime") Date endTime,
                                          @Param("tenantId") Long tenantId);
}


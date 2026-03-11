package com.jsh.erp.datasource.mappers;

import org.apache.ibatis.annotations.Param;

import java.util.Date;
import java.util.List;
import java.util.Map;

public interface CustomerReportMapperEx {
    List<Map<String, Object>> listConsumption(@Param("startTime") Date startTime,
                                             @Param("endTime") Date endTime,
                                             @Param("depotId") Long depotId,
                                             @Param("memberKey") String memberKey);

    List<Map<String, Object>> listCreditBill(@Param("startTime") Date startTime,
                                            @Param("endTime") Date endTime,
                                            @Param("depotId") Long depotId,
                                            @Param("memberKey") String memberKey,
                                            @Param("status") String status);
}

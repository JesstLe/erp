package com.jsh.erp.datasource.mappers;

import com.jsh.erp.datasource.entities.Supplier;
import org.apache.ibatis.annotations.Param;

import java.math.BigDecimal;
import java.util.Date;
import java.util.List;
import java.util.Map;

public interface CustomerDashboardMapperEx {
    Long countMemberTotal();

    BigDecimal sumMemberAdvanceIn();

    Long countConsumerDistinct(@Param("startTime") Date startTime,
                              @Param("endTime") Date endTime,
                              @Param("depotId") Long depotId);

    BigDecimal sumServiceAmount(@Param("startTime") Date startTime,
                               @Param("endTime") Date endTime,
                               @Param("depotId") Long depotId);

    BigDecimal sumProductAmount(@Param("startTime") Date startTime,
                               @Param("endTime") Date endTime,
                               @Param("depotId") Long depotId);

    List<Map<String, Object>> revenueTrend(@Param("startTime") Date startTime,
                                          @Param("endTime") Date endTime,
                                          @Param("depotId") Long depotId);

    List<Supplier> listRecentMembers(@Param("limit") Integer limit);
}

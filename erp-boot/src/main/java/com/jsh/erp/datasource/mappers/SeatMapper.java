package com.jsh.erp.datasource.mappers;

import com.jsh.erp.datasource.entities.Seat;
import org.apache.ibatis.annotations.Param;
import java.util.List;

public interface SeatMapper {
    int deleteByPrimaryKey(Long id);

    int insert(Seat record);

    int insertSelective(Seat record);

    Seat selectByPrimaryKey(Long id);

    int updateByPrimaryKeySelective(Seat record);

    int updateByPrimaryKey(Seat record);

    List<Seat> selectByCondition(@Param("name") String name, 
                                 @Param("depotId") Long depotId, 
                                 @Param("offset") Integer offset, 
                                 @Param("rows") Integer rows);

    List<Seat> selectByConditionWithDepotIds(@Param("name") String name,
                                             @Param("depotIdList") List<Long> depotIdList,
                                             @Param("offset") Integer offset,
                                             @Param("rows") Integer rows);

    Long countsByCondition(@Param("name") String name, 
                           @Param("depotId") Long depotId);

    Long countsByConditionWithDepotIds(@Param("name") String name,
                                       @Param("depotIdList") List<Long> depotIdList);
}
